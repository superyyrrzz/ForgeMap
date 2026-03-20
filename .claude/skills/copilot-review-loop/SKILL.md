---
name: copilot-review-loop
description: >
  Request GitHub Copilot to review a PR and iterate on feedback automatically.
  Use when user says "copilot review", "review loop", "watch this PR",
  "monitor PR for Copilot comments", "iterate on Copilot feedback", or
  "keep fixing Copilot comments".
---

# Copilot Review Loop

Non-blocking loop: schedule 5-min cron → fix Copilot comments each tick → repeat until clean. Copilot auto-review is enabled on this repo, so reviews are triggered automatically on each push — no manual review requests needed.

## Critical: Read `references/copilot-api.md` first

It contains exact API commands, correct author logins per API, and GraphQL queries.
**GraphQL uses `copilot-pull-request-reviewer` (no `[bot]`).** Getting this wrong silently misses all comments.

## Initial invocation

1. Detect PR number (conversation context → explicit mention → `gh pr view --json number`)
2. Detect OWNER/REPO: `gh repo view --json owner,name`
3. Schedule cron: `CronCreate` with `*/5 * * * *`, `recurring: true`
4. Report to user and **return immediately** — never block/poll

## Each cron iteration

### 1. Always fetch unresolved Copilot threads first

Use GraphQL (see references). Filter by `copilot-pull-request-reviewer` (no `[bot]`).
If PR is MERGED/CLOSED → cancel cron and stop.

**Why threads-first**: Copilot's REST review API `commit_id` does NOT reliably update when Copilot leaves inline comments. If you gate on review SHA matching HEAD before checking threads, you will miss comments that Copilot left asynchronously. Always check threads regardless of review SHA.

### 2. If unresolved comments exist → fix them

For each unresolved comment:
- Read the code, assess validity
- Fix if valid; explain and dismiss if not
- Build and test (`dotnet build && dotnet test` or equivalent)
- Reply to comment, then resolve thread

After all comments addressed: commit, push, reset clean counter to 0. Copilot will automatically review the new push.

### 3. If no unresolved comments → check review SHA and count clean

Compare HEAD SHA with Copilot's latest review commit (see references).
- **Match or Copilot has no pending review** → confirmed-clean. After 3 consecutive confirmed-clean iterations → cancel cron, report success.
- **No match** → Copilot has not reviewed yet, do NOT count as clean (wait for next iteration).

## Stop conditions

- 3 consecutive confirmed-clean iterations (with no unresolved threads)
- 20 total iterations
- PR merged/closed
- User cancels via `CronDelete`
