---
name: copilot-review-loop
description: >
  Request GitHub Copilot to review a PR and iterate on feedback automatically.
  Use when user says "copilot review", "review loop", "watch this PR",
  "monitor PR for Copilot comments", "iterate on Copilot feedback", or
  "keep fixing Copilot comments".
---

# Copilot Review Loop

Non-blocking loop: request Copilot review → schedule 5-min cron → fix comments each tick → re-trigger review → repeat until clean.

## Critical: Read `references/copilot-api.md` first

It contains exact API commands, correct author logins per API, and GraphQL queries.
**REST uses `copilot-pull-request-reviewer[bot]`; GraphQL uses `copilot-pull-request-reviewer` (no `[bot]`).** Getting this wrong silently misses all comments.

## Initial invocation

1. Detect PR number (conversation context → explicit mention → `gh pr view --json number`)
2. Detect OWNER/REPO: `gh repo view --json owner,name`
3. Request Copilot review (see `references/copilot-api.md`)
4. Schedule cron: `CronCreate` with `*/5 * * * *`, `recurring: true`
5. Report to user and **return immediately** — never block/poll

## Each cron iteration

### CRITICAL: No blocking, no sleeping, no polling

Each iteration MUST be non-blocking. Check the state once, act or skip, and return immediately. **NEVER use `sleep` or poll in a loop waiting for Copilot to finish reviewing.** The cron fires every 5 minutes — if Copilot hasn't reviewed yet, simply skip the iteration and let the next cron tick handle it. This is the entire point of using a cron-based approach.

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

After all comments addressed: commit, push, reset clean counter to 0, re-trigger Copilot review (MANDATORY — see step 4).

### 3. If no unresolved comments → check review SHA and count clean

Compare HEAD SHA with Copilot's latest review commit (see references).
- **Match** → confirmed-clean. After 3 consecutive confirmed-clean iterations → cancel cron, report success.
- **No match** → Copilot hasn't reviewed HEAD yet. Request review (if not already pending), do NOT count as clean. **Return immediately** — let the next cron tick check again. Do NOT sleep or poll.

### 4. Re-trigger Copilot review (MANDATORY after every push)

Never skip this step — it was the #1 failure mode historically.

## Stop conditions

- 3 consecutive confirmed-clean iterations (with no unresolved threads)
- 20 total iterations
- PR merged/closed
- User cancels via `CronDelete`
