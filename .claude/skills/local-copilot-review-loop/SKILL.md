---
name: local-copilot-review-loop
description: >
  Run the local Copilot CLI to review code, fix findings, and re-review until clean.
  Use when user says "local copilot review", "copilot cli review", "ask copilot to review locally",
  "local review loop", or "run copilot on this code".
  This is for the npm-installed Copilot CLI — NOT the online GitHub Copilot PR reviewer.
---

# Local Copilot CLI Review Loop

Iterative loop: run ACP-based review → fix findings → re-run → repeat until clean.

## Running the CLI

Use the ACP companion script for structured, session-based reviews:

```bash
node ".claude/skills/local-copilot-review-loop/scripts/copilot-acp-companion.mjs" review --base <base_ref>
```

**Options:**
- `--base <ref>` — Git base ref for diff (e.g., `main`, `origin/main`). Omit for working-tree changes.
- `--cwd <path>` — Working directory (default: current directory)
- `--json` — Output structured JSON: `{ review, stopReason, base, exitCode }`
- `--stream` — Stream Copilot's response text to stderr as it arrives
- `--debug` — Dump raw ACP protocol messages to stderr
- `--timeout <ms>` — Timeout in ms (default: 600000 = 10 min)
- Positional args after flags are treated as additional focus text

**Examples:**
```bash
# Review changes against main branch
node ".claude/skills/local-copilot-review-loop/scripts/copilot-acp-companion.mjs" review --base main

# Review staged and unstaged changes vs HEAD (untracked files are not included)
node ".claude/skills/local-copilot-review-loop/scripts/copilot-acp-companion.mjs" review

# JSON output with focus
node ".claude/skills/local-copilot-review-loop/scripts/copilot-acp-companion.mjs" review --base main --json "focus on error handling"
```

**Progress output:** The script always shows progress on stderr (elapsed time, tool calls, thinking summaries). No flags needed for basic visibility.

- The companion script manages the ACP lifecycle internally (connect, session, prompt, cleanup)
- Copilot CLI is started with `--allow-all-tools` for non-interactive review
- `session/request_permission` requests are auto-approved with `allow_once`/`allow_always` as appropriate
- Has a built-in 10-minute timeout; cancels and exits if exceeded
- Findings are reported as `[file:line] severity (high/medium/low): description`

## Workflow

**IMPORTANT: This is a MANDATORY loop. You MUST keep iterating until a stop condition is met. Do NOT stop after a single iteration just because you fixed or dismissed some findings.**

1. **Detect scope**: Determine `--base` ref. For PRs use the PR base branch. For local work use `main` or `origin/main`. Changes must be committed for `--base` to see them.
2. **Run the companion script** with appropriate `--base` flag
3. **Process ALL findings from this iteration**:
   - Read the referenced code at the cited line numbers to verify each finding
   - Fix findings that are valid
   - Dismiss findings that are invalid (note why briefly)
   - Track counts of fixed vs dismissed
4. **If code was changed**: build and test. If build/test fails, stop and surface the error.
5. **If code was changed**: commit the fixes (do NOT push unless user asked)
6. **Re-run the companion script** — this is a NEW ACP session that sees the updated code
7. **Repeat from step 3** until a stop condition is met

### Stop conditions

- CLI output contains no `[file:line]` findings (only praise, "looks good", or "No issues found.")
- ALL findings in the current iteration were already seen AND dismissed in a PREVIOUS iteration of THIS loop (loop detection). "Previous conversation" findings do NOT count — only findings from this invocation.
- 5 iterations reached (safety valve — surface remaining findings to user)
- Build/test fails after a fix (stop, surface error, let user decide)
- CLI times out (report it, suggest retrying)

### What counts as "same finding" for loop detection

Two findings are the same if they reference the same file, similar line range (±10 lines), and describe the same core issue. Cosmetic wording differences don't matter. If the finding is about code you just changed, it's NOT a repeat — re-assess it.

## Output format

After the loop ends, report:

```
Local Copilot CLI review complete.
- Iterations: N
- Findings fixed: X
- Findings dismissed (invalid): Y
- Remaining: Z (if any)
```
