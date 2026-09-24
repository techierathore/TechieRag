# Sevak — decisions I need from you

| | |
|---|---|
| App | Sevak |
| Written | 2026-09-24 |
| Waiting on | 0 decisions. Answered 2026-09-24 and moved here; kept as the record. |

## What happened

While the brainstorm on 24 September 2026 settled how the TechieDesk application becomes Sevak and moves to its own repository, one question had no answer in any plan or document: whether that new repository should be visible to the public from the day it is created, or stay private until the first release.

It is your choice because it is about exposure, not engineering. The release plan already says any secrets left in configuration files must be rotated before a public binary exists. If the repository is public from day one, that rotation has to happen before the move, not before the release.

## What I need you to decide

### 1. Is the Sevak repository public from day one, or private until v0.1?

Sevak's code, documents and history become readable by anyone the moment the repository is public. Nothing else about the work changes either way.

| Option | What happens | What it costs |
|---|---|---|
| **A — Private until v0.1** | The repository stays private through the move, the Windows and Mac work and the offline build. It becomes public in the same step as the first release tag, after secrets are rotated and the hands-on test is done. | Nobody outside can see progress until release. |
| **B — Public from day one** | The repository is public now. Anyone can watch the work as it happens. | Every secret in configuration files must be rotated before the move, today, and every commit from now on is public. |

**My recommendation: A** — the release plan already puts secret rotation before the first public binary, and a private repository keeps that ordering without adding work now.

**Answered 2026-09-24: A, private until v0.1.**

## What I do when you answer

- Record the answer in the update brief and in the Sevak repository's decision log.
- If A: nothing changes now; the repository goes public with the first release tag, after secret rotation and the hands-on test.
- If B: rotate the secrets in configuration files before copying anything into the new repository, then continue.

## Copy this back to me

```
Sevak: decision 1 — go with option A. The repository stays private until v0.1.
```

```
Sevak: decision 1 — go with option B. Make the repository public now; rotate secrets before the move.
```
