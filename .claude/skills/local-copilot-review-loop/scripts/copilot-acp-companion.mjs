#!/usr/bin/env node
/**
 * Copilot ACP Companion — drives GitHub Copilot CLI via the Agent Client
 * Protocol (ACP) over stdio for structured, session-based code reviews.
 *
 * Usage:  node copilot-acp-companion.mjs review [options]
 */

// Suppress Node.js DEP0190 (shell+args) — unavoidable when resolving npm on Windows
process.removeAllListeners("warning");
const originalEmit = process.emit.bind(process);
process.emit = function (event, ...args) {
  if (event === "warning" && args[0]?.code === "DEP0190") return false;
  return originalEmit(event, ...args);
};

import { spawn, spawnSync } from "node:child_process";
import readline from "node:readline";
import process from "node:process";
import path from "node:path";
import fs from "node:fs";

// ---------------------------------------------------------------------------
// Argument parsing (inlined from codex-plugin-cc/scripts/lib/args.mjs)
// ---------------------------------------------------------------------------

function parseArgs(argv, config = {}) {
  const valueOptions = new Set(config.valueOptions ?? []);
  const booleanOptions = new Set(config.booleanOptions ?? []);
  const aliasMap = config.aliasMap ?? {};
  const options = {};
  const positionals = [];
  let passthrough = false;

  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];

    if (passthrough) { positionals.push(token); continue; }
    if (token === "--") { passthrough = true; continue; }
    if (!token.startsWith("-") || token === "-") { positionals.push(token); continue; }

    if (token.startsWith("--")) {
      const [rawKey, inlineValue] = token.slice(2).split("=", 2);
      const key = aliasMap[rawKey] ?? rawKey;

      if (booleanOptions.has(key)) {
        options[key] = inlineValue === undefined ? true : inlineValue !== "false";
        continue;
      }
      if (valueOptions.has(key)) {
        const nextValue = inlineValue ?? argv[index + 1];
        if (nextValue === undefined || (inlineValue === undefined && nextValue.startsWith("-"))) throw new Error(`Missing value for --${rawKey}`);
        options[key] = nextValue;
        if (inlineValue === undefined) index += 1;
        continue;
      }
      positionals.push(token);
      continue;
    }

    const shortKey = token.slice(1);
    const key = aliasMap[shortKey] ?? shortKey;
    if (booleanOptions.has(key)) { options[key] = true; continue; }
    if (valueOptions.has(key)) {
      const nextValue = argv[index + 1];
      if (nextValue === undefined || nextValue.startsWith("-")) throw new Error(`Missing value for -${shortKey}`);
      options[key] = nextValue;
      index += 1;
      continue;
    }
    positionals.push(token);
  }

  return { options, positionals };
}

// ---------------------------------------------------------------------------
// ACP Client
// ---------------------------------------------------------------------------

class AcpClient {
  constructor(cwd) {
    this.cwd = cwd;
    this.proc = null;
    this.rl = null;
    this.pending = new Map();
    this.nextId = 1;
    this.closed = false;
    this.stderr = "";
    this.notificationHandler = null;
    this.exitPromise = new Promise((resolve) => { this.resolveExit = resolve; });
    this.exitResolved = false;
    this.sessionId = null;
    this.debugLog = null;
  }

  async connect() {
    // Resolve the copilot binary. On Windows, prefer the native .exe to avoid
    // shell:true (which triggers Node DEP0190 on recent versions).
    let command = "copilot";
    let useShell = false;
    if (process.platform === "win32") {
      // Try to find the native binary shipped by @github/copilot
      const archPackage = {
        x64: "copilot-win32-x64",
        arm64: "copilot-win32-arm64",
        ia32: "copilot-win32-ia32",
      }[process.arch];
      if (archPackage) {
        const npmRoot = spawnSync("npm", ["root", "-g"], { encoding: "utf8", shell: true }).stdout?.trim();
        if (npmRoot) {
          const nativeBin = path.join(npmRoot, "@github", "copilot", "node_modules",
            "@github", archPackage, "copilot.exe");
          if (fs.existsSync(nativeBin)) {
            command = nativeBin;
          } else {
            useShell = true;
          }
        } else {
          useShell = true;
        }
      } else {
        useShell = true;
      }
    }

    const copilotArgs = ["--acp", "--no-auto-update"];
    if (process.env.COPILOT_ACP_ALLOW_ALL_TOOLS === "1") {
      copilotArgs.push("--allow-all-tools");
    }

    this.proc = spawn(
      command,
      copilotArgs,
      { cwd: this.cwd, stdio: ["pipe", "pipe", "pipe"], ...(useShell ? { shell: true } : {}) }
    );

    this.proc.stdout.setEncoding("utf8");
    this.proc.stderr.setEncoding("utf8");
    this.proc.stderr.on("data", (chunk) => { this.stderr += chunk; });
    this.proc.stdin.on("error", (err) => { if (!this.closed) this.handleExit(err); });
    this.proc.on("error", (err) => this.handleExit(err));
    this.proc.on("exit", (code, signal) => {
      this.handleExit(
        code === 0
          ? null
          : new Error(`copilot --acp exited (${signal ? `signal ${signal}` : `code ${code}`})`)
      );
    });

    this.rl = readline.createInterface({ input: this.proc.stdout });
    this.rl.on("line", (line) => this.handleLine(line));

    // ACP handshake
    await this.request("initialize", {
      protocolVersion: 1,
      clientInfo: { name: "copilot-acp-companion", version: "1.0.0" },
    });
    this.notify("notifications/initialized", {});
  }

  async newSession() {
    const result = await this.request("session/new", {
      cwd: this.cwd,
      mcpServers: [],
    });
    this.sessionId = result.sessionId;
    return result;
  }

  async prompt(text, { onChunk } = {}) {
    if (!this.sessionId) throw new Error("No active session. Call newSession() first.");

    let fullText = "";
    const prevHandler = this.notificationHandler;
    const pendingTools = new Map(); // toolCallId → { title, kind, path, cmd }

    this.notificationHandler = (msg) => {
      if (
        msg.method === "session/update" &&
        msg.params?.sessionId === this.sessionId
      ) {
        const update = msg.params.update;
        // ACP sends text chunks as update.content.text or update.text depending on version
        const chunkText = update?.content?.text ?? update?.text;
        if (update?.sessionUpdate === "agent_message_chunk" && chunkText) {
          fullText += chunkText;
          if (onChunk) onChunk("text", chunkText);
        } else if (update?.sessionUpdate === "agent_thought_chunk" && onChunk) {
          const thoughtText = update?.content?.text ?? update?.text;
          if (thoughtText) onChunk("thought", thoughtText);
        } else if (update?.sessionUpdate === "tool_call" && onChunk) {
          const id = update.toolCallId ?? "";
          const kind = update.kind ?? "";
          const path = update.rawInput?.path ?? update.locations?.[0]?.path ?? "";
          const cmd = update.rawInput?.command ?? "";
          const title = update.title ?? update.rawInput?.description ?? "";
          const shortPath = path ? path.split(/[\\/]/).slice(-3).join("/") : "";
          const shortCmd = cmd ? (cmd.length > 80 ? cmd.slice(0, 80) + "..." : cmd) : "";
          const label = kind === "read" && shortPath ? `Reading ${shortPath}`
            : kind === "execute" && shortCmd ? `Running: ${shortCmd}`
            : title || `${kind || "tool"} call`;
          pendingTools.set(id, { label, kind });
          onChunk("tool_start", label);
        } else if (update?.sessionUpdate === "tool_call_update" && onChunk) {
          const id = update.toolCallId ?? "";
          const status = update.status ?? "";
          const info = pendingTools.get(id);
          const label = info?.label ?? id.slice(0, 12);
          if (status === "completed") {
            pendingTools.delete(id);
            onChunk("tool_done", label);
          } else if (status === "failed") {
            pendingTools.delete(id);
            const reason = update.rawOutput?.message ?? "unknown";
            onChunk("tool_fail", `${label}: ${reason}`);
          }
        }
        return;
      }
      if (prevHandler) prevHandler(msg);
    };

    try {
      const result = await this.request("session/prompt", {
        sessionId: this.sessionId,
        prompt: [{ type: "text", text }],
      });
      return { fullText, stopReason: result.stopReason ?? "unknown" };
    } finally {
      this.notificationHandler = prevHandler;
    }
  }

  async cancel() {
    if (this.sessionId && !this.closed) {
      try {
        await this.request("session/cancel", { sessionId: this.sessionId });
      } catch {
        // best-effort
      }
    }
  }

  async close() {
    if (this.closed) { await this.exitPromise; return; }
    this.closed = true;
    if (this.rl) this.rl.close();
    if (this.proc && !this.proc.killed) {
      this.proc.stdin.end();
      const timer = setTimeout(() => {
        if (this.proc && !this.proc.killed) this.proc.kill("SIGTERM");
      }, 50);
      timer.unref?.();
    }
    await this.exitPromise;
  }

  // --- internal plumbing ---

  request(method, params) {
    if (this.closed) throw new Error("ACP client is closed.");
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject, method });
      try {
        this.sendMessage({ jsonrpc: "2.0", id, method, params });
      } catch (err) {
        this.pending.delete(id);
        reject(err);
      }
    });
  }

  notify(method, params = {}) {
    if (this.closed) return;
    this.sendMessage({ jsonrpc: "2.0", method, params });
  }

  sendMessage(msg) {
    if (!this.proc?.stdin) throw new Error("ACP stdin not available.");
    this.proc.stdin.write(JSON.stringify(msg) + "\n");
  }

  handleLine(line) {
    if (!line.trim()) return;
    let msg;
    try { msg = JSON.parse(line); } catch { return; }

    if (this.debugLog) this.debugLog(msg);

    // Server-initiated request (has both id and method) — e.g. session/request_permission
    if (msg.id !== undefined && msg.method) {
      if (msg.method === "session/request_permission") {
        // Auto-approve with allow_once (scoped to this request only)
        const options = msg.params?.options ?? [];
        const allowOnce = options.find(o => o.kind === "allow_once");
        if (allowOnce) {
          this.sendMessage({
            jsonrpc: "2.0",
            id: msg.id,
            result: { optionId: allowOnce.optionId },
          });
        } else {
          // No allow_once available — reject to fail closed
          const reject = options.find(o => o.kind === "reject_once") ?? options[options.length - 1];
          this.sendMessage({
            jsonrpc: "2.0",
            id: msg.id,
            result: { optionId: reject?.optionId ?? "reject_once" },
          });
        }
      } else {
        // Unknown server request — respond with method not found
        this.sendMessage({
          jsonrpc: "2.0",
          id: msg.id,
          error: { code: -32601, message: "Method not found" },
        });
      }
      return;
    }

    // Response to a request we sent
    if (msg.id !== undefined && !msg.method) {
      const entry = this.pending.get(msg.id);
      if (!entry) return;
      this.pending.delete(msg.id);
      if (msg.error) {
        const err = new Error(msg.error.message ?? `ACP ${entry.method} failed`);
        err.data = msg.error;
        entry.reject(err);
      } else {
        entry.resolve(msg.result ?? {});
      }
      return;
    }

    // Notification from agent
    if (msg.method && this.notificationHandler) {
      this.notificationHandler(msg);
    }
  }

  handleExit(error) {
    if (this.exitResolved) return;
    this.exitResolved = true;
    this.closed = true;
    for (const entry of this.pending.values()) {
      entry.reject(error ?? new Error("ACP connection closed."));
    }
    this.pending.clear();
    this.resolveExit();
  }
}

// ---------------------------------------------------------------------------
// Git helpers
// ---------------------------------------------------------------------------

const MAX_DIFF_BYTES = 100 * 1024;

function git(args, cwd) {
  const result = spawnSync("git", args, {
    cwd,
    encoding: "utf8",
    maxBuffer: 2 * 1024 * 1024,
  });
  if (result.error) {
    throw new Error(`Failed to run "git ${args.join(" ")}": ${result.error.message}`);
  }
  if (result.status !== 0) {
    const stderr = (result.stderr ?? "").trim();
    throw new Error(`"git ${args.join(" ")}" exited with status ${result.status}${stderr ? `: ${stderr}` : ""}`);
  }
  return result.stdout ?? "";
}

function collectDiff(cwd, base) {
  let diff;
  if (base) {
    diff = git(["diff", "--no-color", "--no-ext-diff", `${base}...HEAD`], cwd);
  } else {
    diff = git(["diff", "--no-color", "--no-ext-diff", "HEAD"], cwd);
  }
  if (!diff.trim()) return null;
  if (Buffer.byteLength(diff) > MAX_DIFF_BYTES) {
    diff = diff.slice(0, MAX_DIFF_BYTES) + "\n\n[... diff truncated at 100 KB ...]";
  }
  return diff;
}

function getRepoRoot(cwd) {
  return git(["rev-parse", "--show-toplevel"], cwd).trim() || cwd;
}

// ---------------------------------------------------------------------------
// Review prompt
// ---------------------------------------------------------------------------

function buildReviewPrompt(diffText, options = {}) {
  const focus = options.focus ? `\nAdditional focus: ${options.focus}\n` : "";
  return `Review the following code changes. Focus on bugs, logic errors, security issues, performance problems, and code quality concerns.

For each finding, report it as:
[file:line] severity (high/medium/low): description

If there are no issues, say "No issues found."
${focus}
\`\`\`\`diff
${diffText}
\`\`\`\``;
}

// ---------------------------------------------------------------------------
// Review handler
// ---------------------------------------------------------------------------

async function handleReview(argv) {
  const { options, positionals } = parseArgs(argv, {
    valueOptions: ["cwd", "base", "timeout"],
    booleanOptions: ["json", "stream", "debug"],
    aliasMap: { C: "cwd" },
  });

  const cwd = path.resolve(options.cwd ?? process.cwd());
  const base = options.base != null && String(options.base).trim() !== "" ? String(options.base).trim() : null;
  const jsonOutput = Boolean(options.json);
  const stream = Boolean(options.stream);
  const debug = Boolean(options.debug);
  let timeoutMs = 600000;
  if (options.timeout !== undefined) {
    const raw = String(options.timeout);
    timeoutMs = Number(raw);
    if (!Number.isFinite(timeoutMs) || !Number.isInteger(timeoutMs) || timeoutMs <= 0 || raw !== String(timeoutMs)) {
      const msg = `Invalid --timeout value: "${options.timeout}" (must be a positive integer in ms)`;
      process.stderr.write(msg + "\n");
      if (jsonOutput) {
        console.log(JSON.stringify({ review: null, stopReason: null, base: base ?? "working-tree", error: msg, exitCode: 1 }));
      }
      process.exitCode = 1;
      return;
    }
  }
  const focus = positionals.join(" ").trim() || null;

  // Collect diff
  let diff;
  try {
    diff = collectDiff(cwd, base);
  } catch (err) {
    process.stderr.write(`Diff collection error: ${err.message}\n`);
    if (jsonOutput) {
      console.log(JSON.stringify({ review: null, stopReason: null, base: base ?? "working-tree", error: err.message, exitCode: 1 }));
    }
    process.exitCode = 1;
    return;
  }
  if (!diff) {
    const msg = "No changes found to review.";
    if (jsonOutput) {
      console.log(JSON.stringify({ review: msg, stopReason: null, base: base ?? "working-tree", exitCode: 0 }));
    } else {
      console.log(msg);
    }
    return;
  }

  const prompt = buildReviewPrompt(diff, { focus });

  // ACP session
  const client = new AcpClient(getRepoRoot(cwd));
  if (debug) {
    client.debugLog = (msg) => {
      // For session/update notifications, dump the full update object
      if (msg.method === "session/update" && msg.params?.update) {
        const u = msg.params.update;
        // Truncate large text fields to keep output readable
        const sanitized = JSON.stringify(u, (key, val) => {
          if (typeof val === "string" && val.length > 300) return val.slice(0, 300) + "...[truncated]";
          return val;
        });
        process.stderr.write("[debug:update] " + sanitized + "\n");
      } else if (msg.id !== undefined && msg.method) {
        // Server-initiated request (e.g. session/request_permission)
        const text = JSON.stringify(msg);
        process.stderr.write("[debug:server-request] " + (text.length > 1000 ? text.slice(0, 1000) + "..." : text) + "\n");
      } else if (msg.id !== undefined && !msg.method) {
        // Response to our request
        const text = JSON.stringify(msg);
        process.stderr.write("[debug:response] " + (text.length > 1000 ? text.slice(0, 1000) + "..." : text) + "\n");
      }
    };
  }
  let timedOut = false;
  const timer = setTimeout(() => {
    (async () => {
      try {
        timedOut = true;
        process.stderr.write("Review timed out. Cancelling...\n");
        if (jsonOutput) {
          console.log(JSON.stringify({ review: null, stopReason: null, base: base ?? "working-tree", error: "timeout", exitCode: 1 }));
        }
        await client.cancel();
        await client.close();
        process.exitCode = 1;
      } catch (err) {
        process.stderr.write(`ACP timeout error: ${err.message}\n`);
      }
    })();
  }, timeoutMs);
  timer.unref?.();

  try {
    await client.connect();
    const startTime = Date.now();
    const elapsed = () => `${((Date.now() - startTime) / 1000).toFixed(0)}s`;
    process.stderr.write(`[copilot] Connected. Reviewing...\n`);
    await client.newSession();

    let phase = "thinking";
    let thoughtBuffer = "";
    let lastThoughtSummary = "";

    const onChunk = (kind, data) => {
      if (kind === "text") {
        // Response text is the final review — just accumulate (shown on stdout at end)
        // But if --stream, also show it live
        if (stream) process.stderr.write(data);
      } else if (kind === "thought") {
        thoughtBuffer += data;
        // Extract meaningful sentences from thought buffer to show phase changes
        const sentences = thoughtBuffer.split(/[.!?]\s+/);
        if (sentences.length > 1) {
          const latest = sentences[sentences.length - 2].trim().replace(/\s+/g, " ");
          if (latest && latest.length > 20 && latest !== lastThoughtSummary) {
            lastThoughtSummary = latest;
            if (phase !== "thinking") {
              phase = "thinking";
            }
            process.stderr.write(`[${elapsed()}] ${latest}.\n`);
          }
          thoughtBuffer = sentences[sentences.length - 1]; // keep incomplete sentence
        }
      } else if (kind === "tool_start") {
        phase = "investigating";
        process.stderr.write(`[${elapsed()}] ${data}\n`);
      } else if (kind === "tool_done") {
        // Silently complete — the start message was enough
      } else if (kind === "tool_fail") {
        process.stderr.write(`[${elapsed()}] Failed: ${data}\n`);
      }
    };

    const result = await client.prompt(prompt, { onChunk });
    clearTimeout(timer);

    process.stderr.write(`[${elapsed()}] Review complete.\n`);

    if (timedOut) return;

    if (jsonOutput) {
      console.log(JSON.stringify({
        review: result.fullText,
        stopReason: result.stopReason,
        base: base ?? "working-tree",
        exitCode: 0,
      }));
    } else {
      console.log(result.fullText);
    }
  } catch (err) {
    clearTimeout(timer);
    if (timedOut) return;
    process.stderr.write(`ACP error: ${err.message}\n`);
    if (client.stderr) process.stderr.write(client.stderr);
    if (jsonOutput) {
      console.log(JSON.stringify({ review: null, stopReason: null, base: base ?? "working-tree", error: err.message, exitCode: 1 }));
    }
    process.exitCode = 1;
  } finally {
    await client.close();
  }
}

// ---------------------------------------------------------------------------
// Entry point
// ---------------------------------------------------------------------------

async function main() {
  const [subcommand, ...argv] = process.argv.slice(2);

  switch (subcommand) {
    case "review":
      await handleReview(argv);
      break;
    default: {
      const usage =
        `Usage: copilot-acp-companion.mjs review [options]\n` +
        `\nSubcommands:\n  review   Run a Copilot code review via ACP\n` +
        `\nOptions:\n` +
        `  --cwd <path>     Working directory (default: cwd)\n` +
        `  --base <ref>     Git base ref for diff (default: working tree)\n` +
        `  --json           Output structured JSON\n` +
        `  --stream         Stream progress to stderr as chunks arrive\n` +
        `  --timeout <ms>   Timeout in ms (default: 600000)\n`;
      if (subcommand) {
        process.stderr.write(usage);
        process.exitCode = 1;
      } else {
        process.stdout.write(usage);
      }
    }
  }
}

main().catch((err) => {
  process.stderr.write(`Fatal: ${err.message}\n`);
  process.exitCode = 1;
});
