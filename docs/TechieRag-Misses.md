# TechieRag — Misses

| | |
|---|---|
| App | TechieRag |
| Count | 3 logged: 0 open, 3 fixed, 0 will not fix |
| Source | `docs/metrics/misses.jsonl`, one row per miss record. Rewritten by `tf-misses-md.sh` on every new record. Never edit it: a wrong row is corrected by a new record. |
| Updated | 2026-09-07 |

**Whose gap** answers the four questions of the miss protocol: **the app's spec** did not say it, so the checklist line is fixed; **the framework never said it**, so one requirement line and a check are added; **the check was too weak** (a review, or a script that did not fire), so the check is fixed; **said and ignored**, so the rule becomes a hook or is deleted. **not sorted** means the record predates the sort or nobody has answered yet; `bash .tfcore/utils/tf-emit.sh --amend <miss> sort <spec|unsaid|weak-check|ignored>` completes it.

## Fixed (3)

| Miss | Found | Closed | Whose gap | What went wrong |
|---|---|---|---|---|
| MISS-TechieRag-20260903-03 (REQ-FN-004) | 2026-09-03 by agent-review | 2026-09-03 by fix-issues | not sorted | no sentence recorded (hallucinated-api, other, why: insufficient-verify-method) |
| MISS-TechieRag-20260903-02 | 2026-09-03 by owner | 2026-09-03 by fix-issues | not sorted | no sentence recorded (unspecified-gap, brd, why: missing-checklist-item) |
| MISS-TechieRag-20260903-01 (REQ-FN-003) | 2026-09-03 by owner | 2026-09-03 by fix-issues | not sorted | no sentence recorded (regression, config, why: insufficient-verify-method) |
