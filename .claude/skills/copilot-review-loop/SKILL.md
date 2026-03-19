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

### 1. Gate: Has Copilot reviewed HEAD?

Compare HEAD SHA with Copilot's latest review commit (see references).
- **Match** → proceed to step 2
- **No match** → request review, **skip this iteration entirely**. Do not check comments. Do not count as clean.

### 2. Fetch unresolved Copilot threads

Use GraphQL (see references). Filter by `copilot-pull-request-reviewer` (no `[bot]`).
If PR is MERGED/CLOSED → cancel cron and stop.

### 3. No comments → count as confirmed-clean

Only if step 1 confirmed Copilot reviewed HEAD. After 3 consecutive confirmed-clean iterations → cancel cron, report success.

### 4. Fix each comment

For each unresolved comment:
- Read the code, assess validity
- Fix if valid; explain and dismiss if not
- Build and test (`dotnet build && dotnet test` or equivalent)
- Reply to comment, then resolve thread

### 5. Commit, push, reset clean counter to 0

### 6. Re-trigger Copilot review (MANDATORY after every push)

Never skip this step — it was the #1 failure mode historically.

## Stop conditions

- 3 consecutive confirmed-clean iterations
- 20 total iterations
- PR merged/closed
- User cancels via `CronDelete`
