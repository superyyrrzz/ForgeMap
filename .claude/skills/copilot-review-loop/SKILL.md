---
name: copilot-review-loop
description: >
  Use this skill when the user asks to "start Copilot review", "monitor PR for Copilot comments",
  "review loop", "copilot review loop", "iterate on Copilot feedback", or after creating a PR
  when the user wants Copilot to review it and iterate on feedback. Also activates when the user
  says "watch this PR", "keep fixing Copilot comments", or similar.
---

# Copilot Review Loop

Request GitHub Copilot to review a PR, then monitor for comments on a **non-blocking** 5-minute cron loop.
The initial invocation only requests the review and schedules the cron — it does NOT block the chat waiting
for Copilot. The cron fires every 5 minutes and handles the full check→fix→push→re-trigger cycle.

## PR number detection

Determine the PR number from context using this priority:
1. **Conversation context**: If a PR was just created in this session (e.g. via `/commit-push-pr`), use that PR number.
2. **Explicit mention**: If the user mentioned a PR number, use that.
3. **Current branch**: Fall back to `gh pr view --json number --jq '.number'` to detect from the current branch.

## Repository detection

Detect the GitHub repository owner and name:
```
gh repo view --json owner,name --jq '"\(.owner.login)/\(.name)"'
```

## Copilot author login — CRITICAL

**The REST API and GraphQL API return DIFFERENT logins for Copilot:**
- **REST API** (reviews endpoint): `copilot-pull-request-reviewer[bot]`
- **GraphQL API** (reviewThreads → comments → author): `copilot-pull-request-reviewer` (NO `[bot]` suffix)

When filtering comments from each API, use the correct login string for that API.
Getting this wrong causes comments to be silently missed.

## Copilot review request

**The correct reviewer name for requesting reviews is `copilot-pull-request-reviewer[bot]`.**
Using just `"copilot"` silently returns HTTP 200 but does NOT actually add Copilot as a reviewer.

```
gh api repos/{OWNER}/{REPO}/pulls/{PR_NUMBER}/requested_reviewers \
  -f "reviewers[]=copilot-pull-request-reviewer[bot]" --method POST
```

## Initial invocation (non-blocking)

When the skill is first invoked:

1. **Detect** PR number, OWNER, REPO.
2. **Request** Copilot review (see above).
3. **Schedule** the cron loop immediately using `CronCreate` with `*/5 * * * *` and `recurring: true`.
4. **Report** to the user: "Copilot review requested. Monitoring cron scheduled every 5 minutes. You can continue working."
5. **Do NOT block** — do not poll or wait. Return control to the user immediately.

## Per-iteration procedure (runs in cron)

Execute this exact sequence every cron tick. **Every step is mandatory — never skip the re-trigger step.**

### Step 1 — Check if Copilot has reviewed the latest commit

```
# Get the HEAD commit SHA
HEAD_SHA=$(gh pr view {PR_NUMBER} --json headRefOid --jq '.headRefOid')

# Get Copilot's most recent review commit (REST API uses [bot] suffix)
gh api repos/{OWNER}/{REPO}/pulls/{PR_NUMBER}/reviews \
  --jq '[.[] | select(.user.login == "copilot-pull-request-reviewer[bot]")] | sort_by(.submitted_at) | last | .commit_id'
```

- If Copilot's latest review `commit_id` matches `HEAD_SHA` → proceed to Step 2.
- If it does NOT match → request a new review, then **skip this iteration**. Do NOT check for comments, do NOT count as clean. The next cron tick will re-check.

**Why**: Checking comments before Copilot finishes reviewing finds zero comments and falsely counts as "clean", causing premature loop exit.

### Step 2 — Fetch unresolved threads

Use the GraphQL API to get all unresolved review threads:

```
gh api graphql -f query='
{
  repository(owner: "OWNER", name: "REPO") {
    pullRequest(number: PR_NUMBER) {
      state
      reviewThreads(last: 50) {
        nodes {
          id
          isResolved
          comments(first: 1) {
            nodes {
              databaseId
              body
              createdAt
              author { login }
            }
          }
        }
      }
    }
  }
}'
```

**Filter**: unresolved threads whose first comment author is `copilot-pull-request-reviewer` (NO `[bot]` — GraphQL omits it).

Also check `state` — if the PR is `MERGED` or `CLOSED`, stop the loop immediately.

### Step 3 — If no unresolved Copilot comments

Only count as clean if Step 1 confirmed Copilot reviewed the HEAD commit.

Report "No unresolved Copilot review comments. PR is clean. (Consecutive clean: N/3)"

If this is the 3rd consecutive confirmed-clean iteration, **stop the loop** (cancel the cron job). Otherwise let cron continue.

### Step 4 — For each unresolved comment

For each unresolved Copilot comment, evaluate whether the suggestion makes sense:

a. **Read the referenced file and line** to understand the current code.
b. **Assess the comment**: Is it a valid concern? If it's a false positive or doesn't apply, reply explaining why and resolve the thread (skip to step 4e).
c. **Fix the code**: Make the minimal change to address the concern.
d. **Verify**: Run `dotnet build` and `dotnet test` (or the project's equivalent build/test commands). If tests fail, investigate and fix.
e. **Reply to the comment**: Use `gh api repos/{OWNER}/{REPO}/pulls/{PR_NUMBER}/comments/{COMMENT_ID}/replies` with a brief explanation of what was fixed (or why it was dismissed) and the commit SHA.
f. **Resolve the thread**: Use the GraphQL `resolveReviewThread` mutation with the thread ID.

### Step 5 — Commit and push (once, after all comments are addressed)

Stage all changed files, create a single commit with a descriptive message, and push:

```
git add <changed files>
git commit -m "<message>"
git push origin <branch>
```

**Reset the consecutive clean counter to 0** (since we just pushed new code that needs review).

### Step 6 — Re-trigger Copilot review (MANDATORY)

**This step must ALWAYS execute after pushing, with no exceptions.**

```
gh api repos/{OWNER}/{REPO}/pulls/{PR_NUMBER}/requested_reviewers \
  -f "reviewers[]=copilot-pull-request-reviewer[bot]" --method POST
```

Verify the response contains `copilot-pull-request-reviewer`. If it fails, retry once.

### Step 7 — Done

The cron loop handles scheduling the next iteration automatically.

## Important safeguards

1. **Author login differs by API**: REST uses `copilot-pull-request-reviewer[bot]`, GraphQL uses `copilot-pull-request-reviewer`. Using the wrong one silently misses all comments.
2. **Always use `copilot-pull-request-reviewer[bot]`** for requesting reviews — `"copilot"` silently fails.
3. **Never skip the re-trigger step** — this was the #1 failure mode. Even if no comments were found, if you just pushed a fix, ALWAYS re-trigger.
4. **NEVER count an iteration as "clean" unless Copilot has reviewed the HEAD commit** — this was the #2 failure mode.
5. **Non-blocking**: Never poll/block the chat. If Copilot hasn't reviewed yet, skip the iteration and let the next cron tick handle it.
6. **Max consecutive clean iterations**: 3 confirmed-clean → stop loop and report success.
7. **Max total iterations**: Stop after 20 to prevent infinite loops. Report remaining unresolved comments if any.
8. **Build verification**: Always build and test after code changes, before committing.

## Stop conditions

The loop stops when ANY of these are true:
- 3 consecutive **confirmed-clean** iterations (Copilot reviewed HEAD and left no new comments)
- 20 total iterations reached
- The PR is merged or closed
- User cancels via `CronDelete`
