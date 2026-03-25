---
name: local-copilot-review-loop
description: >
  Run the local Copilot CLI to review code, fix findings, and re-review until clean.
  Use when user says "local copilot review", "copilot cli review", "ask copilot to review locally",
  "local review loop", or "run copilot on this code".
  This is for the npm-installed Copilot CLI — NOT the online GitHub Copilot PR reviewer.
---

# Local Copilot CLI Review Loop

Iterative loop: run the **local** Copilot CLI → Claude Code fixes findings → re-run CLI → repeat until clean.

## CRITICAL: Two different "Copilots" — do not confuse them

| | Local Copilot CLI (THIS skill) | Online Copilot PR Reviewer (`copilot-review-loop` skill) |
|---|---|---|
| **What** | npm-installed CLI (`copilot` on PATH; locate via `where copilot` / `which copilot`) | GitHub's built-in PR review bot |
| **Invocation** | `copilot -p "..."` in terminal | `gh api .../requested_reviewers` REST call |
| **How it reads code** | Reads files directly from disk | Reads PR diff via GitHub API |
| **Output** | Stdout text (findings, suggestions) | PR review comments (GraphQL threads) |
| **Identity** | N/A — local process | `copilot-pull-request-reviewer[bot]` (REST) / `copilot-pull-request-reviewer` (GraphQL) |
| **Models used** | gpt-5.4, claude-haiku-4.5, claude-sonnet-4.5 (auto-selected) | Unknown (GitHub-managed) |
| **Loop mechanism** | Synchronous: run → fix → re-run | Cron-based: push → wait → check threads |

**If the user says just "copilot review"** without "local" or "cli", prefer the online `copilot-review-loop` skill. Only use THIS skill when the user explicitly mentions "local", "cli", or references the `copilot` command-line tool.

## Initial invocation

1. Detect what to review:
   - If a PR exists: review the PR diff (`gh pr diff` piped or file list)
   - Otherwise: review staged/unstaged changes (`git diff`)
   - User may also specify particular files
2. Determine the scope for the prompt (specific files, entire diff, or a spec document)
3. Run the first review (see "Running the CLI" below)
4. Process findings synchronously — this is NOT cron-based

## Running the CLI

### Basic invocation

```bash
copilot -p "Review the pull request #<PR> on <OWNER>/<REPO>. Focus on <scope>. Cross-reference against <context files> for correctness."
```

### Repo detection friction

The local CLI auto-detects the repo from `git remote -v`, but may resolve to **an outdated or redirected repo name**. If the CLI reports a 404 or wrong repo:

```bash
# Check what the CLI will see:
git remote get-url origin

# If it shows an old name (e.g., TypeForge instead of ForgeMap),
# the CLI usually self-corrects, but verify in its output.
```

This is a known friction point — the CLI may initially try the wrong repo URL before self-correcting.

### Prompt tips

- Be specific about what to review: "Review `docs/SPEC-v1.3-auto-wiring.md` against the actual generator code in `src/ForgeMap.Generator/ForgeCodeEmitter.cs`"
- Ask for cross-referencing: "Verify that behavioral claims in the spec match the implementation"
- Request structured output: "List each finding as: [file:line] issue description"

### Timeout

The CLI can take 30-90 seconds per invocation (it runs multiple LLM calls internally). Set a 120-second timeout on the shell command to avoid hanging:

```
# When invoking via a tool that supports a timeout parameter, set it to 120000 ms.
# Do NOT use the Unix `timeout` command (unavailable on Windows).
```

## Processing findings

For each finding in the CLI output:

1. **Assess validity**: Read the referenced code and determine if the finding is correct
2. **Fix if valid**: Make the code change
3. **Dismiss if invalid**: Note why (false positive, stale context, etc.)
4. **Track**: Keep a count of valid vs dismissed findings

After processing all findings:
- Build and test if code was changed (`dotnet build && dotnet test` or equivalent)
- Commit and push if code was changed

## Re-review loop

After fixing findings:

1. Re-run the CLI with the same prompt (or narrowed scope if only a few files changed)
2. Process new findings
3. Repeat until the CLI returns **no actionable findings**

### Stop conditions

- CLI output contains no code findings (only praise or "looks good")
- CLI returns the same findings it already returned (loop detection — stop and surface to user)
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

## Key differences from online review loop

| Aspect | Local CLI (this skill) | Online reviewer |
|--------|----------------------|-----------------|
| Blocking model | **Synchronous** — run, wait, process, repeat | **Async cron** — schedule, fire every 5 min |
| API calls | None — pure local process | REST + GraphQL to GitHub API |
| Thread resolution | N/A — no threads to resolve | Must resolve GraphQL threads |
| Review request | N/A — just re-run the CLI | Must `POST .../requested_reviewers` after each push |
| Scope control | Full — you craft the prompt | Limited — Copilot reviews entire PR diff |
| Cost | Uses user's Copilot CLI quota | Uses GitHub's Copilot review quota |
