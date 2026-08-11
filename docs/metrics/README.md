# docs/metrics — development telemetry

Append-only JSONL. **Tracked by git on purpose** — this is the project's own
development history, and it is the one thing the framework cannot reconstruct
after the fact.

| File | One record per | Written by |
|---|---|---|
| `runs.jsonl` | framework command run | the task, at completion |
| `gates.jsonl` | REQ verdict per verify run — **the primary stream** | `verify-phase` §6a, `triage-issues` |
| `sessions.jsonl` | agent session | the `SessionEnd` hook |
| `commits.jsonl` | commit | the repo's own `post-commit` hook |

Schema, enums, and every known limitation: `.tfcore/telemetry/SCHEMA.md`.
Report: `/TechieFlow:agents:flow-master *metrics <AppName>` → `METRICS.md`.

**All four files are created empty, on purpose, and an empty one is not a fault.**
The installer seeds the set so every repo has the same shape and no writer has to
guess whether its stream exists. A stream stays at zero bytes until something
actually happens: `gates.jsonl` until the first `*verify`, `runs.jsonl` until the
first framework command, `sessions.jsonl` until the first agent session ends, and
`commits.jsonl` until your first commit after telemetry was installed. Commit
these empty files along with the rest — a tracked empty stream is what makes the
first record a one-line diff instead of a new file appearing from nowhere.

**Never edit these files by hand, never sort them, never compact them.** They are
a log. Rewriting one destroys exactly the history it exists to keep.

**No secrets, no content, no client data** — records carry IDs, counts, durations,
verdicts and file paths at most. Never requirement text, prompt text, file
contents, or commit subjects. Assume every line here could become public.

## Working on more than one machine

`.gitattributes` gives these streams `merge=union`, so two machines appending to
the same file keep **both** sides' lines instead of conflicting. Pull, push, carry
on — you never hand-resolve a log, which is the one way records get silently
dropped. Union merge can leave a record duplicated or out of chronological order;
every consumer sorts on `ts` and de-duplicates commits on `sha`, so neither costs
you anything.

**`commits.jsonl` needs no collecting.** The `post-commit` hook *reconciles*: on
each commit it appends a record for every commit reachable from HEAD that the file
does not already have. So after you pull another machine's work, your next commit
here backfills all of it. The commit log is itself an append-only log that push
and pull already replicate everywhere; this stream is a projection of it.

Two things worth knowing:

- The record for the newest commit lands in the **next** commit — the hook fires
  after the commit is sealed. Nothing is lost; whichever machine commits next
  writes it.
- The hook lives in `.git/hooks/`, which is **not** part of the repository, so
  every clone needs its own. `update-framework.sh <repo>` installs it, and
  `tf-metrics.sh --report` warns when the clone you are standing in has none.

To fill in a machine's history immediately rather than waiting for a commit:

    .tfcore/telemetry/tf-metrics.sh --backfill-commits .

Idempotent — already-recorded shas are skipped — so run it as often as you like.
It is also why the hook is optional: delete it and reconcile by hand instead.
