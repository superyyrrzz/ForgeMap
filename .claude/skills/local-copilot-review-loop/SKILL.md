---
name: local-copilot-review-loop
description: >
  Run the local Copilot CLI to review code, fix findings, and re-review until clean.
  Use when user says "local copilot review", "copilot cli review", "ask copilot to review locally",
  "local review loop", or "run copilot on this code".
  This is for the npm-installed Copilot CLI — NOT the online GitHub Copilot PR reviewer.
---

# Local Copilot CLI Review Loop

Iterative loop: run `copilot -p "..."` → fix findings → re-run → repeat until clean.

## Running the CLI

```bash
copilot -p "Review the pull request #<PR> on <OWNER>/<REPO>. Focus on <scope>. Cross-reference against <context files> for correctness."
```

- The `-p` flag passes a prompt directly; output is plain text to stdout
- The CLI auto-detects the repo from `git remote -v`
- Execution time varies: simple reviews ~1–2 minutes, cross-referencing prompts can take 5+ minutes. Set a 600-second (10 min) timeout. If it exceeds that, kill and retry with a narrower prompt.
- Be specific: name files to review, ask for cross-referencing, request structured output like `[file:line] issue`

## Workflow

1. **Detect scope**: PR diff (`gh pr diff`), staged/unstaged changes (`git diff`), or user-specified files
2. **Run the CLI** with a focused prompt
3. **Process findings**:
   - Assess validity by reading the referenced code
   - Fix if valid; dismiss if not (note why)
   - Track counts of fixed vs dismissed
4. **Build and test** if code was changed
5. **Commit and push** if code was changed
6. **Re-run the CLI** — repeat until no actionable findings

### Stop conditions

- CLI output contains no code findings (only praise or "looks good")
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
