# Local Copilot CLI Reference

## Installation

The Copilot CLI is installed globally via npm. Locate it with:

```bash
where copilot    # Windows
which copilot    # macOS / Linux
```

## Invocation

```bash
copilot -p "<prompt>"
```

The `-p` flag passes a prompt directly. The CLI:
1. Auto-detects the git repo from `git remote -v`
2. May use multiple models internally
3. Outputs findings as plain text to stdout
4. Exit code 0 on success regardless of findings

## Known issues

### Repo name resolution
The CLI reads `git remote -v` to detect the repo. If the repo was recently renamed, the CLI may initially try the old name. It usually self-corrects via GitHub's redirect, but verify in the output if you see 404 errors.

### Timeout
Typical execution time: 30–90 seconds. Set a 120s timeout to be safe.

### Output format
The CLI output is unstructured text — not JSON. Parse findings by looking for patterns like:
- File paths with line numbers
- Suggestions phrased as "consider", "should", "could"
- Severity indicators (if any)

The exact format varies by model and prompt.
