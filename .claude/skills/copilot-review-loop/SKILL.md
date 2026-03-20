---
name: copilot-review-loop
description: >
  Request GitHub Copilot to review a PR and iterate on feedback automatically.
  Use when user says "copilot review", "review loop", "watch this PR",
  "monitor PR for Copilot comments", "iterate on Copilot feedback", or
  "keep fixing Copilot comments".
---

# Copilot Review Loop

Non-blocking loop: request Copilot review → schedule 5-min cron → fix comments each tick → re-trigger review → repeat until Copilot says "generated no comments".

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

After all comments addressed: commit, push, re-trigger Copilot review (MANDATORY — see step 4).

### 3. If no unresolved comments → check Copilot's latest review

Fetch Copilot's latest review via REST (see references). Check two things:
- **`commit_id`** matches HEAD SHA
- **`body`** contains `"generated no comments"`

If **both** match → Copilot reviewed the latest code and found nothing. **Cancel cron and report success.**

If commit_id does not match HEAD → Copilot hasn't reviewed the latest push yet. Request Copilot review (re-requesting is acceptable even if already pending). **Return immediately** — let the next cron tick check again. Do NOT sleep or poll.

If commit_id matches but body does NOT contain "generated no comments" → Copilot reviewed but found issues:
- Re-fetch unresolved threads. If threads now exist, process them as in step 2.
- If **no inline threads exist**, Copilot's feedback is only in the top-level review body. Surface the body text to the user and pause — do not keep looping expecting inline comments that may never appear.

### 4. Re-trigger Copilot review (MANDATORY after every push)

Never skip this step — it was the #1 failure mode historically.

## Stop conditions

- Copilot's latest review on HEAD contains "generated no comments"
- PR merged/closed
- User cancels via `CronDelete`
- **Safety valve**: CronCreate auto-expires recurring jobs after 3 days. As an additional guard, stop after 20 cron iterations and surface a warning to the user if termination conditions were never met.
