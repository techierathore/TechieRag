# Verification endpoints — where and how to configure them

> **Who this is for:** the owner of a machine that runs TechieFlow's build/verify gates.
> **What it changes:** the gates stop guessing which services exist on this host and read a declared list instead.
> **The file:** `.tfcore/core-config.yaml`, under `runtimeVerification.services`.

## Why this exists

The smoke and verify gates need real services to prove real behaviour — an LLM to stream an answer, Docker to
run Qdrant, a Postgres to exercise `PgVectorStore`. Until now they went looking for them: `curl :11434` for
Ollama, `:1234` for LM Studio, the default Docker socket. When those guesses missed, the gate wrote

> no LLM provider is reachable on this host

into a requirement's remarks. That sentence is doing two wrong things at once:

1. **It cannot find a service that isn't on a default port**, or one running on another machine on the LAN.
   This project's own LM Studio lives at `192.168.1.13:1234` — no amount of localhost probing finds it.
2. **It turns a fact about the machine into something that reads like a defect.** "Not reachable" sounds like
   something broke. "This host has no LLM configured" is the actual state, and it is the owner's decision,
   not a discovery.

So the endpoints are now **declared**. A host either offers a service or it does not, and it says which.

## Where to put it

`.tfcore/core-config.yaml` — the same file that already carries the Appium block. The `services:` key sits
beside `appium:`:

```yaml
runtimeVerification:
  services:
    llm:
      kind: lmstudio
      url: http://192.168.1.13:1234/v1
      model: qwen2.5-coder-32b-instruct
      healthPath: /models
      toolCalling: true
  appium:
    maccatalyst:
      url: http://127.0.0.1:4723
      # …
```

The shipped file has every key present but **commented out**, with the values this project has used
historically as examples. They are deliberately not enabled: an endpoint nobody confirmed is a guess with
extra steps. Uncomment what this machine actually offers.

## The three states, and what each one does to a gate

This is the whole contract:

| State | What the gate does |
|---|---|
| **Key present** | Uses that endpoint verbatim. Falling back to a conventional port when it doesn't answer is a defect, not a fallback — it hides a misconfiguration behind a lucky guess. |
| **Key absent, blank, or commented out** | The host does not offer it. The dependent gate degrades to `⚠ STATIC-ONLY` (or owner-UAT) and says *"no LLM endpoint is configured for this host (`runtimeVerification.services.llm` is unset)"*. **Never a faked pass.** |
| **Key present but unreachable** | A **real failure**, reported as one. Something the host promised is down, and that deserves attention in a way an unset key never does. |

## Secrets

**`.tfcore/core-config.yaml` is committed.** REQ-NFR-002 says no committed secrets, so no credential is ever
written into it. Instead a `*Env` key names the environment variable that holds the value, and the gate reads
the variable at run time:

```yaml
    qdrant:
      url: http://127.0.0.1:6333
      apiKeyEnv: TechieRagQdrantApiKey     # the NAME of the variable, never the key
```

```bash
# set it where your shell will pick it up — ~/.zshrc, a .env you don't commit, or the launch environment
export TechieRagQdrantApiKey='…'
```

The same rule covers `apiKeyEnv`, `passwordEnv`, and `connectionStringEnv`. A literal key, password or
connection string must never appear in the config, a test, a checklist remark, or a log line.

## The services

### `llm` — unblocks the largest group of requirements

Seven REQs sit at `Needs re-verify` purely because no model could be reached:
`REQ-RAG-001`, `-002`, `-005`, `-006`, `-010`, `-021`, `-023`.

```yaml
    llm:
      kind: lmstudio                     # lmstudio | ollama | openai-compatible | azure | anthropic | gemini
      url: http://192.168.1.13:1234/v1   # base URL INCLUDING the API prefix the kind expects
      model: qwen2.5-coder-32b-instruct
      healthPath: /models                # GET url+healthPath must return 200 for the head to count as up
      apiKeyEnv: TechieRagLlmApiKey   # omit entirely when the provider needs no key
      toolCalling: true                  # false for a model that can't call tools → tool gates degrade honestly
```

`toolCalling: false` matters: `REQ-RAG-006` (tool demo + execution trace) and `REQ-RAG-021` (`@agent`
invocation) need a model that actually emits tool calls. Declaring the limitation gets an honest
`⚠ STATIC-ONLY` instead of a confusing failure.

Check it yourself before relying on it:

```bash
curl -s http://192.168.1.13:1234/v1/models | head
```

### `docker` — gates Qdrant and the Testcontainers path

```yaml
    docker:
      host: unix:///var/run/docker.sock   # or tcp://host:2375 for a remote / rootless daemon
```

Docker is currently **not installed** on this machine (`command not found`), which is why `REQ-RAG-044`'s
PgVector-against-real-Postgres path is impossible here rather than merely inconvenient. Leave this key
commented until Docker exists — an unset key produces the honest degrade, a set-but-broken one produces a
failure report.

### `qdrant`, `postgres`

```yaml
    qdrant:
      url: http://127.0.0.1:6333
      apiKeyEnv: TechieRagQdrantApiKey

    postgres:
      connectionStringEnv: TechieRagTestPostgres   # the ONLY way a connection string reaches a gate
```

`postgres` is what `REQ-RAG-044` needs to stop being `PARTIAL`: `PgVectorStore` has never run against a real
server.

### `appManager`, `imap`

```yaml
    appManager:
      url: http://192.168.1.14:5101
      apiKeyEnv: TechieDeskAppManagerKey

    imap:
      host: imap.example.test
      port: 993
      usernameEnv: TechieRagImapUser
      passwordEnv: TechieRagImapPassword
```

`imap` covers the live half of `REQ-RAG-049`; the mbox transport needs nothing configured and is exercised
against real bytes today.

## Where the rule is enforced

`.tfcore/tasks/_smoke-test-policy.md` §"Service endpoints are CONFIGURED, never discovered" — the shared rule
included by every task that builds or verifies code, and the one that sub-agent prompts must carry verbatim.

It does **not** weaken the existing "I can't run it here is a banned excuse" rule. Booting a service the host
declared is still the agent's job; what changed is that it learns the address from config instead of guessing.
A service that is genuinely not configured was never the agent's to invent.

## Related

- `.tfcore/core-config.yaml` — the config itself, with commented examples for every key.
- `WORKFLOW.html` §0b — one-time host setup for the Appium heads.
- `docs/TechieDesk-Checklist.md` — the REQ rows whose status depends on these endpoints.
