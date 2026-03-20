# Copilot API Reference

## Auto-review

Copilot auto-review is enabled on this repo. Reviews are triggered automatically on each push — no manual `requested_reviewers` API calls needed.

## Author login per API

| API | Login |
|-----|-------|
| REST (reviews) | `copilot-pull-request-reviewer[bot]` |
| GraphQL (reviewThreads) | `copilot-pull-request-reviewer` |

## Check if Copilot reviewed a specific commit

```bash
HEAD_SHA=$(gh pr view {PR} --json headRefOid --jq '.headRefOid')

gh api repos/{OWNER}/{REPO}/pulls/{PR}/reviews \
  --jq '[.[] | select(.user.login == "copilot-pull-request-reviewer[bot]")] | sort_by(.submitted_at) | last | .commit_id'
```

## Fetch unresolved threads (GraphQL)

```graphql
{
  repository(owner: "OWNER", name: "REPO") {
    pullRequest(number: PR) {
      state
      reviewThreads(last: 50) {
        nodes {
          id
          isResolved
          comments(first: 1) {
            nodes { databaseId body createdAt author { login } }
          }
        }
      }
    }
  }
}
```

Filter: `isResolved == false` AND `author.login == "copilot-pull-request-reviewer"` (no `[bot]`).

## Reply to a comment

```bash
gh api repos/{OWNER}/{REPO}/pulls/{PR}/comments/{COMMENT_ID}/replies \
  -f body="<message>"
```

## Resolve a thread

```graphql
mutation { resolveReviewThread(input: {threadId: "THREAD_ID"}) { thread { isResolved } } }
```
