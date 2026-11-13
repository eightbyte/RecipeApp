# Llm — structured output from the Model Server

Inference runs in the **Model Server**: llama.cpp's `llama-server`, started by hand with
`tools/llm-server/start.ps1` (see `tools/llm-server/README.md`) and called over HTTP. The API holds
no model, no GPU libraries and no CUDA runtime. No cloud calls. Used by Scraping, from a URL or
from pasted text (`RecipeScrapeService`), and the seed pipeline's LLM stages. ADR 0002 records why.

| Type | Role |
|---|---|
| `ILlmStructuredClient` | The abstraction consumers take. Tests use `StubLlmStructuredClient` |
| `ModelServerClient` | Posts to `/v1/chat/completions` with the schema's GBNF as `grammar`, and parses `choices[0].message.content` |
| `JsonSchemaGrammar` | JSON Schema → GBNF converter (supports `enum`, `["type","null"]`) |
| `IModelServerStatusProbe` / `ModelServerStatusProbe` | `/health` then `/v1/models`, giving a `ModelServerStatus` (state + model name). Tests use `StubModelServerStatusProbe` |
| `ModelServerState` | `offline`, `loading`, `ready`, `unauthorized`. `UnavailableReason()` holds the wording the frontend repeats in `src/constants/modelServer.js` |
| `ModelServerUnavailableException` | Thrown by the client for no HTTP answer (offline: refused, or dropped because the server was stopped mid-generation), 401 (unauthorized) or 503 (loading) |
| `LlmOptions` / `ModelServerOptions` / `LlmSamplingOptions` | Bound to `Llm:ModelServer:*` and `Llm:Sampling:*` |

## Configuration

- `Llm:ModelServer:BaseUrl`: empty means there is no Model Server, so the status reads `offline`
  and Scraping returns 503. Development uses `http://localhost:9931`. Production reads
  `MODEL_SERVER_URL` from `.env` (default `http://host.docker.internal:9931`).
- `Llm:ModelServer:ApiKey`: sent as a Bearer token when set. Production reads
  `MODEL_SERVER_API_KEY`. It must match the server's `ApiKey` setting.
- `Llm:ModelServer:StatusTimeoutSeconds` (default 3): how long a status check waits before
  reporting `offline`.
- `Llm:Sampling:*` holds llama.cpp's own defaults and is **sent with every request**, so the
  server's settings never decide them. **This is a measured result, not an oversight:** colder
  decoding made extraction worse, because retries stopped recovering reproducible misreads.

## Rules for the client

- **Send the grammar as `grammar`, never `response_format`.** `JsonSchemaGrammar` is the measured
  converter; llama-server's own schema conversion is a different grammar.
- **The named `HttpClient` has no timeout.** Each caller's `LlmTimeoutSeconds` budget cancels the
  request, and llama-server stops generating when a non-streaming request is cancelled.
- **An unavailable Model Server is not a failed recipe.** Callers let
  `ModelServerUnavailableException` through every per-item catch: the seed stages stop the run
  without recording a failure or counting toward `MaxConsecutiveLlmFailures`, and Scraping answers
  503 rather than 422.
- **Readiness never depends on the Model Server.** The updater rolls back on a failed
  `/health/ready`, and the server is off most of the time. Check the status endpoint
  (`GET /api/v1/model-server/status`) instead.
- The prompt goes through the GGUF's own chat template with thinking off (`--reasoning off` on the
  server, `chat_template_kwargs.enable_thinking=false` per request). That adds an empty think
  block; the benchmark in ADR 0002 passed with it.

## Rules learned the hard way

- **Model choice dominates prompt tuning.** Moving from Qwen2.5 7B to Qwen3.5 9B took Stage 4
  from 85.7% to 96.8% and made it faster. If quality regresses, suspect the model file before
  the prompt.
- **`JsonSchemaGrammar` omits non-required properties from the grammar entirely.** An optional
  property is one the model *cannot emit*, not one it may skip. Make a field required and
  nullable (`["string","null"]`) when the model should be able to say "none".
- **A required non-nullable field forces the model to invent a value.** `amount` became nullable
  for this reason.
- **Label input items with the exact index you want back** (`[0]`, `[1]`, …). Asking for 0-based
  positions over 1-numbered lines produced silent off-by-one linkage.
- **Never give the model a menu of candidate values.** It stops reading the source and picks from
  the menu instead.
- Worked examples teach whatever they happen to show. Check that an example's edge case teaches
  the rule you intend, not an incidental property of the example.
- Derive prompt vocabularies from code (for example `MeasurementUnit.AcceptedSpellings`), never
  from hand-typed lists that can drift.
