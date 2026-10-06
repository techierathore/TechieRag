# TechieFlow feedback — found while building TechieRag

| | |
|---|---|
| App | TechieRag |
| Upstream | TechieFlow |
| Updated | 2026-10-06 |

## Summary

1 entry: 0 blocking now, 1 filed and not blocking, 0 fixed upstream.

Nothing is blocked.

## Entries

### TF-001 — the feedback-file reader marks an open entry "fixed" when an old reply line says "all others fixed" and a newer reply names a later entry

- **Severity:** major
- **Blocks:** no — the reply for TR-RAG-048 was written inside the entry instead of under "Replies from TechieRag", and the work carried on.
- **Repro:** in `Sevak/docs/Sevak-TechieRag-Feedback.md`, the "Replies from TechieRag" section keeps an old summary verbatim that contains "All others fixed app-side". Add a reply block there naming `TR-RAG-048`, then run:
  ```text
  bash .tfcore/utils/tf-feedback.sh Sevak
  TR-RAG-047   fixed   …   (it is open; nothing replied to it)
  ```
- **Expected:** TR-RAG-047 stays open. Only entries a reply names, or entries covered by an "everything else is fixed" sentence in the same reply block, become fixed.
- **Actual:** `replied_fixed` in `tf_feedback.py` treats the whole "## Replies from …" section as one block. Its "all others fixed" rule then covers every entry up to the highest number mentioned anywhere in that section, so a new reply drags every earlier open entry with it. `tf-phase.sh start` in Sevak would then tell agents "never report TR-RAG-047 as open".
- **Encountered in:** `*triage-and-fix TechieRag` for Sevak TR-RAG-048, 2026-10-06.
- **Workaround:** the TR-RAG-048 reply lives in the entry (a status line at the top of the entry and a dated fix section at its end); no reply block names it.
- **Suggested fix:** split "## Replies from …" into its dated "### " blocks and apply the "all others fixed" rule within one block only, bounded by the ids that same block names. Also read the fix marker in the entry heading, not only in the first 2,500 characters of the body, and do not match it inside quoted or code text.

## Replies from TechieFlow

<!-- The upstream team's answers, newest block first. Left in full: this is the record. -->
