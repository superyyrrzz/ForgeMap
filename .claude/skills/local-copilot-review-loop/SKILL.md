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
- `--timeout <ms>` — Timeout in ms (default: 600000 = 10 min)
- Positional args after flags are treated as additional focus text

**Environment variables:**
- `COPILOT_ACP_ALLOW_ALL_TOOLS=1` — Passes `--allow-all-tools` to Copilot CLI, broadening tool-execution permissions. Only enable in trusted, controlled environments.

**Examples:**
```bash
# Review changes against main branch
node ".claude/skills/local-copilot-review-loop/scripts/copilot-acp-companion.mjs" review --base main

# Review staged and unstaged changes vs HEAD (untracked files are not included)
node ".claude/skills/local-copilot-review-loop/scripts/copilot-acp-companion.mjs" review

# JSON output with focus
node ".claude/skills/local-copilot-review-loop/scripts/copilot-acp-companion.mjs" review --base main --json "focus on error handling"
```

- The companion script manages the ACP lifecycle internally (connect, session, prompt, cleanup)
- Has a built-in 10-minute timeout; cancels and exits if exceeded
- Findings are reported as `[file:line] severity (high/medium/low): description`

## Workflow

1. **Detect scope**: PR diff (`gh pr diff`), staged/unstaged changes (`git diff`), or user-specified files
2. **Run the companion script** with appropriate `--base` flag
3. **Process findings**:
   - Assess validity by reading the referenced code
   - Fix if valid; dismiss if not (note why)
   - Track counts of fixed vs dismissed
4. **Build and test** if code was changed
5. **Commit and push** if code was changed
6. **Re-run the companion script** — repeat until no actionable findings

### Stop conditions

- CLI output contains no code findings (only praise or "looks good" or "No issues found." or "No changes found to review.")
- CLI returns the same findings it already returned (loop detection)
- 5 iterations reached (safety valve — surface remaining findings to user)
- Build/test fails after a fix (stop, surface error, let user decide)

## Output format

After the loop ends, report:

```
Local Copilot CLI review complete.
- Iterations: N
- Findings fixed: X
- Findings dismissed (invalid): Y
- Remaining: Z (if any)
```
