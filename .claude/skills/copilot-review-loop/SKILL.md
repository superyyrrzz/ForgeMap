---
name: copilot-review-loop
description: >
  Use this skill when the user asks to "start Copilot review", "monitor PR for Copilot comments",
  "review loop", "copilot review loop", "iterate on Copilot feedback", or after creating a PR
  when the user wants Copilot to review it and iterate on feedback. Also activates when the user
  says "watch this PR", "keep fixing Copilot comments", or similar.
---

# Copilot Review Loop

Request GitHub Copilot to review a PR, then monitor for comments on a 5-minute loop.
Each iteration: ensure review is requested, **wait for review completion**, check for unresolved
comments, fix valid issues, reply, resolve threads, and re-trigger Copilot review — repeating
until no new comments appear.

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

## Copilot review request — CRITICAL details

**The correct reviewer name is `copilot-pull-request-reviewer[bot]`.**
Using just `"copilot"` silently returns HTTP 200 but does NOT actually add Copilot as a reviewer. Always use the full bot name:

```
gh api repos/{OWNER}/{REPO}/pulls/{PR_NUMBER}/requested_reviewers \
  -f "reviewers[]=copilot-pull-request-reviewer[bot]" --method POST
```

## Per-iteration procedure

Execute this exact sequence every iteration. **Every step is mandatory — never skip the re-trigger step.**

### Step 1 — BLOCK until Copilot has reviewed the latest commit

Check whether Copilot has already reviewed the PR's latest (HEAD) commit:

```
# Get the HEAD commit SHA
HEAD_SHA=$(gh pr view {PR_NUMBER} --json headRefOid --jq '.headRefOid')

# Get Copilot's most recent review and the commit it was submitted on
gh api repos/{OWNER}/{REPO}/pulls/{PR_NUMBER}/reviews \
  --jq '[.[] | select(.user.login == "copilot-pull-request-reviewer[bot]")] | sort_by(.submitted_at) | last | .commit_id'
```

**CRITICAL: You MUST wait for Copilot to finish reviewing the HEAD commit before proceeding.**

- If Copilot's latest review `commit_id` matches `HEAD_SHA` → proceed to Step 2.
- If Copilot's latest review `commit_id` does NOT match `HEAD_SHA`, or Copilot has never reviewed:
  1. Request a new review (see "Copilot review request" above).
  2. **Poll in a loop**: sleep 30 seconds, then re-check the review `commit_id`. Repeat until it matches `HEAD_SHA` or you have waited **5 minutes total** (10 retries × 30s).
  3. If after 5 minutes Copilot still hasn't reviewed, **skip this iteration entirely** — do NOT check for comments, do NOT count it as a clean iteration. Just let the cron reschedule.

**Why this matters**: If you check for unresolved comments before Copilot has finished reviewing, you'll find zero comments and incorrectly count the iteration as "clean". This causes the loop to exit prematurely while Copilot's review comments are still incoming.

### Step 2 — Fetch unresolved threads

Use the GraphQL API to get all unresolved review threads on the PR:

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

Filter to only unresolved threads whose first comment author is `copilot-pull-request-reviewer[bot]`.

Also check `state` — if the PR is `MERGED` or `CLOSED`, stop the loop immediately.

### Step 3 — If no unresolved Copilot comments

Only count as a clean iteration if **Step 1 confirmed Copilot reviewed the HEAD commit**.

Report "No unresolved Copilot review comments. PR is clean. (Consecutive clean: N/3)"

If this is the 3rd consecutive clean iteration, **stop the loop** (cancel the cron job). Otherwise, let the cron continue.

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

Use the full bot name — `"copilot"` alone silently fails:

```
gh api repos/{OWNER}/{REPO}/pulls/{PR_NUMBER}/requested_reviewers \
  -f "reviewers[]=copilot-pull-request-reviewer[bot]" --method POST
```

Verify the response contains `copilot-pull-request-reviewer`. If it fails, retry once.

### Step 7 — Schedule next check

The cron loop handles this automatically. The next iteration will pick up any new comments from the re-triggered review.

## Loop setup

After the first iteration completes, use `CronCreate` to schedule recurring checks at **5-minute intervals**:
- Cron expression: `*/5 * * * *`
- Prompt: the full iteration procedure above, parameterized with OWNER, REPO, and PR number
- Set `recurring: true`

## Important safeguards

1. **Always use `copilot-pull-request-reviewer[bot]`** — never use just `"copilot"` as the reviewer name. The short name silently returns 200 but does nothing.
2. **Never skip the re-trigger step** — this was the #1 failure mode in manual runs. Even if no comments were found, if you just pushed a fix, ALWAYS re-trigger.
3. **Atomic iterations** — each iteration must complete the full check→fix→push→reply→resolve→re-trigger cycle before the next one starts.
4. **NEVER count an iteration as "clean" unless Copilot has reviewed the HEAD commit** — this was the #2 failure mode. The cron would fire, see 0 unresolved comments (because Copilot hadn't reviewed yet), count it as clean, and prematurely exit after 3 such false-clean iterations.
5. **Max consecutive clean iterations**: If 3 consecutive iterations find no new comments AND Copilot has reviewed the HEAD commit in each, stop the loop and report success.
6. **Max total iterations**: Stop after 20 total iterations to prevent infinite loops. Report remaining unresolved comments if any.
7. **Build verification**: Always build and test after code changes, before committing.

## Stop conditions

The loop stops when ANY of these are true:
- 3 consecutive **confirmed-clean** iterations (Copilot reviewed HEAD and left no new comments)
- 20 total iterations reached
- The PR is merged or closed
- User cancels via `CronDelete`
