# Local Copilot CLI Reference

## Installation

The local Copilot CLI is installed globally via npm:

```
C:\ProgramData\global-npm\copilot
```

It is separate from `gh copilot` (GitHub CLI extension) and the online Copilot PR reviewer.

## Naming disambiguation

There are **three** things called "Copilot" in this ecosystem:

| Name | What it is | How to invoke |
|------|-----------|---------------|
| **Copilot CLI** (local) | npm-installed CLI tool | `copilot -p "..."` |
| **GitHub Copilot PR reviewer** (online) | GitHub's automated code review bot | `gh api .../requested_reviewers` |
| **gh copilot** | GitHub CLI extension for Copilot chat | `gh copilot suggest "..."` |

This skill uses the **first one only**.

## Invocation pattern

```bash
copilot -p "<prompt>"
```

The `-p` flag passes a prompt directly. The CLI:
1. Auto-detects the git repo from `git remote -v`
2. May use multiple models (gpt-5.4, claude-haiku-4.5, claude-sonnet-4.5) internally
3. Outputs findings as plain text to stdout
4. Exit code 0 on success regardless of findings

## Known issues

### Repo name resolution
The CLI reads `git remote -v` to detect the repo. If the repo was recently renamed (e.g., `TypeForge` → `ForgeMap`), the CLI may initially try the old name. It usually self-corrects via GitHub's redirect, but verify in the output if you see 404 errors.

### Timeout
Typical execution time: 30-90 seconds. Set a 120s timeout to be safe.

### Output format
The CLI output is unstructured text — not JSON. Parse findings by looking for patterns like:
- File paths with line numbers
- Suggestions phrased as "consider", "should", "could"
- Severity indicators (if any)

The exact format varies by model and prompt.
