---
status: proposed
date: 2026-11-05
issue: "#46"
---

# Model inference runs in a separate Model Server, started on demand

The API ran the model in-process through LLamaSharp with the CUDA 12 backend. The production API
runs in a Docker container with no GPU, so it ships without a model and Scraping is unavailable
there. We move inference into a separate **Model Server**: llama.cpp's own `llama-server`, from a
pinned release, started by hand when it's needed. The API calls it over HTTP, reports whether it
is available, and works normally without it. LLamaSharp is removed from the API.

The model is not always needed. Scraping is occasional and `seed-recipes` is rare, so the model
should hold the GPU only while someone has started it.

## Context

Measured on 2026-11-05 on `develop` at `d5ba6dd`, Windows 11 with Docker Desktop 29.5.2:

- **Without a model the API already works, but Scraping fails late and vaguely.**
  `RecipeScrapeService` fetches the page (15 s timeout, plus up to 20 s for a headless render)
  before it calls the model. A missing model then surfaces as the same 422 as a real extraction
  failure: "Recipe extraction failed. Please try again or create the recipe manually." The UI has
  no way to know in advance.
- **The `llm-model` readiness check is a placeholder.** It always reports Healthy. The updater
  (`deploy/update.ps1`) waits for `/health/ready` after a deploy and treats a non-200 as a
  failed update.
- **A container reaches a host service bound to loopback.** A listener bound to `127.0.0.1` on
  the host was reachable from a `--rm` container at `http://host.docker.internal:<port>`.
- **A container reaches tailnet addresses.** A listener bound to this PC's Tailscale IP was
  reachable from a `--rm` container both by IP and by MagicDNS name. That is this PC's own tailnet
  address. Another tailnet machine goes out through the same Tailscale adapter, but that path
  hasn't been tested.

## Decision

- **The Model Server is `llama-server`, not "any OpenAI-compatible API".** The API sends the GBNF
  that `JsonSchemaGrammar` produces as `grammar`, which is a llama.cpp extension. That keeps the
  grammar the prompts were tuned against. In particular, `JsonSchemaGrammar` leaves optional
  properties out, so the model cannot emit them, and the prompts depend on that.
  `llama-server`'s own `json_schema` converter lets the model emit them.
- **The chat template comes from the GGUF.** The API sends messages to `/v1/chat/completions`,
  so changing the model is a change to the start script only. The API's `ChatTemplate` setting
  is removed, and with it the silent fall-back to ChatML for an unrecognised value.
- **Sampling stays with the API** and is sent with every request, because its values were
  measured against these prompts. Everything about loading the model (file, context size, GPU
  layers, `-np 1`) belongs to the start script.
- **Started and stopped by hand** with PowerShell scripts in `tools/llm-server/`. It doesn't shut
  down when idle, and nothing starts it automatically.
- **It runs on the same PC or another machine on the tailnet.** It binds to `127.0.0.1` by
  default. On another machine it binds to that machine's Tailscale IP, and the start script
  refuses any non-loopback bind unless an API key is set. The public internet is not supported:
  the traffic is plain HTTP, and only the tailnet encrypts it.
- **One instance serves dev and production.** With `-np 1` they queue.
- **The API knows whether it is available.** A status endpoint reports `offline`, `loading`,
  `ready` or `unauthorized`, plus the loaded model's name. Scraping checks the status before
  fetching the page and fails fast with 503. The URL sheet shows the status before a URL is
  entered. `seed-recipes` checks first and exits 1. `/health/ready` never depends on it.

## Considered options

**A. Keep LLamaSharp in the API and pass the GPU through to the container.** Docker Desktop's
WSL2 backend can expose an NVIDIA GPU to containers.
*Cost:* a CUDA base image for the API, a much larger image, GPU reservation in Compose.
*Rejected:* the model would load whenever the API starts, which is the opposite of on-demand,
and the GPU would be tied to the production stack's lifecycle.

**B. Ollama.** Rejected by the maintainer as a solution. It also brings its own model store and
daemon.

**C. A custom companion app wrapping LLamaSharp.** A small .NET host exposing
`ILlmStructuredClient` over HTTP.
*Rejected:* it would mostly copy `LlamaModelHolder` and `LLamaSharpStructuredClient` into a
second app to build, version and update, and `llama-server` already provides it. Both use the
same engine and read the same GGUF.

**D. Accept any OpenAI-compatible endpoint** (LM Studio, vLLM, cloud APIs) and send the schema as
`response_format: json_schema`.
*Rejected for now:* each provider handles schemas differently and none reproduces
`JsonSchemaGrammar`'s optional-property behaviour, so the prompts would need re-tuning and a
benchmark per provider. Another `ILlmStructuredClient` implementation can add this later.

**E. Keep both in-process and server providers behind `Llm:Provider`.**
*Rejected:* two inference paths that can drift. Once the benchmark passes against the server,
the in-process path adds nothing. The `Provider` setting goes too, since a setting with one valid
value can only be set wrong.

**F. Wake on request:** a small always-on listener on the host starts `llama-server` when the API
calls. *Rejected:* it is a host-side companion app again, and starting it by hand is enough.

## Consequences

- **Benchmark gate.** LLamaSharp is deleted only after `seed-recipes --normalise --force
  --limit 30`, run against the Model Server on a copy of the seed cache, clears the 95% bar
  (LLamaSharp scored 96.8%). Model choice has mattered more than prompt tuning (Qwen2.5 7B to
  Qwen3.5 9B took Stage 4 from 85.7% to 96.8%), so a change of engine has to be measured. If the
  GGUF's own chat template is the cause of a failure, the API falls back to building the prompt
  and sending it to `/completion`. The outcome is recorded here before this ADR is accepted.
- The API image drops the LLamaSharp and CUDA packages, and the API never touches the GPU, so dev
  runs no longer need to suppress the model load.
- Production reads `MODEL_SERVER_URL` and `MODEL_SERVER_API_KEY` from `.env`. Moving the Model
  Server to another machine is a `.env` edit and a container restart.
- Updating llama.cpp is a deliberate change to the pinned release, followed by the benchmark.
- With `-np 1`, a production Scrape that arrives during a dev `seed-recipes` run waits for the
  recipe in progress.
- The API does not enforce which model is loaded. It reports and logs the model's name, so a
  quality regression can be traced to the model file first.
