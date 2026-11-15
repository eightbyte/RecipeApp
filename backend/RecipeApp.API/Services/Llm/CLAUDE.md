# Llm — structured output from an LLM provider

Two providers sit behind `ILlmStructuredClient`:

- the **Model Server**: llama.cpp's `llama-server` on this network, started by hand with
  `tools/llm-server/start.ps1` (see `tools/llm-server/README.md`) and called over HTTP. The API
  holds no model, no GPU libraries and no CUDA runtime. ADR 0002 records why.
- the **Claude API** (Anthropic), a cloud provider, through the official `Anthropic` NuGet package.
  ADR 0004 records why.

**Only Scraping follows `Llm:Provider`.** Scraping (`RecipeScrapeService`, from a URL or pasted
text) takes the unkeyed `ILlmStructuredClient` and `ILlmStatusProbe`, which resolve to the
configured provider. The seed pipeline (`SeedRecipeNormaliser`, `IngredientCatalogueBuilder`,
`SeedRecipesCommand`) asks for the Model Server by key
(`[FromKeyedServices(LlmProvider.ModelServer)]`), because its prompts and gates were measured
against it. `LlmServiceCollectionExtensions.AddLlm` holds every registration.

| Type | Role |
|---|---|
| `ILlmStructuredClient` | The abstraction consumers take. Tests use `StubLlmStructuredClient` |
| `ModelServerClient` | Posts to `/v1/chat/completions` with the schema's GBNF as `grammar`, and parses `choices[0].message.content` |
| `JsonSchemaGrammar` | JSON Schema → GBNF converter (supports `enum`, `["type","null"]`) |
| `AnthropicStructuredClient` | Streams a Messages request with the schema as structured outputs and reads the final message's text |
| `StructuredOutputSchema` | Fits a schema to the subset structured outputs accept: closes every object, writes type arrays as `anyOf` |
| `AnthropicConnection` | Builds the SDK client and maps an SDK failure to the `LlmState` it proves |
| `ILlmStatusProbe` / `ModelServerStatusProbe` / `AnthropicStatusProbe` | Give an `LlmStatus` (provider, state, model name). Model Server: `/health` then `/v1/models`. Claude: the Models API for the configured model. Tests use `StubLlmStatusProbe` |
| `LlmProvider` | `modelServer`, `anthropic` |
| `LlmState` | `offline`, `loading`, `ready`, `unauthorized`, `busy`, `notConfigured`. `UnavailableReason(provider)` holds the wording the frontend repeats in `src/constants/llm.js` |
| `LlmUnavailableException` | The provider can't take the request at all (see the state mapping below) |
| `LlmRequestRejectedException` | Claude refused the request itself (400): a bug in what the app sent |
| `LlmOptions` | Bound to `Llm:*`: `Provider`, `ModelServer` (with `Sampling`), `Anthropic` |

## Configuration

- `Llm:Provider`: `ModelServer` (default) or `Anthropic`. Production reads `LLM_PROVIDER` from
  `.env`. It changes on restart only.
- `Llm:ModelServer:BaseUrl`: empty means there is no Model Server, so the status reads `offline`.
  Development uses `http://localhost:9931`. Production reads `MODEL_SERVER_URL` (default
  `http://host.docker.internal:9931`).
- `Llm:ModelServer:ApiKey`: sent as a Bearer token when set. Production reads
  `MODEL_SERVER_API_KEY`. It must match the server's `ApiKey` setting.
- `Llm:ModelServer:StatusTimeoutSeconds` (default 3): how long a status check waits.
- `Llm:ModelServer:Sampling:*` holds llama.cpp's own defaults and is **sent with every Model Server
  request**, so the server's settings never decide them. **This is a measured result, not an
  oversight:** colder decoding made extraction worse, because retries stopped recovering
  reproducible misreads.
- `Llm:Anthropic:ApiKey`: empty means `notConfigured`. Development keeps it in
  `dotnet user-secrets` (`dotnet user-secrets set "Llm:Anthropic:ApiKey" <key>`) or the
  git-ignored `appsettings.Development.json`. Production reads `ANTHROPIC_API_KEY`. Never put it in
  the committed `appsettings.json`.
- `Llm:Anthropic:Model` (`claude-opus-5-5`), `Effort` (`low`), `TimeoutSeconds` (60, the whole
  budget with retries), `MaxRetries` (2, the SDK's), `ThinkingHeadroomTokens` (8000) and
  `StatusTimeoutSeconds` (3).

## Rules for the Model Server client

- **Send the grammar as `grammar`, never `response_format`.** `JsonSchemaGrammar` is the measured
  converter; llama-server's own schema conversion is a different grammar.
- **The named `HttpClient` has no timeout.** Each caller's `LlmTimeoutSeconds` budget cancels the
  request, and llama-server stops generating when a non-streaming request is cancelled.
- The prompt goes through the GGUF's own chat template with thinking off (`--reasoning off` on the
  server, `chat_template_kwargs.enable_thinking=false` per request). That adds an empty think
  block; the benchmark in ADR 0002 passed with it.

## Rules for the Claude client

- **Structured outputs, never tool use.** The schema goes in `output_config.format` after
  `StructuredOutputSchema.Fit`. Forced `tool_choice` returns a 400 on Opus 5.5.
- **No `thinking` setting and no sampling fields.** Thinking can't be turned off on Opus 5.5, and
  current models answer `temperature`/`top_k`/`top_p` with a 400. `Effort` is the only depth
  control.
- **`max_tokens` is the caller's limit plus `ThinkingHeadroomTokens`**, because thinking tokens
  count against it.
- **Stream, then read the final message** (`CreateStreaming(...).Aggregate()`), and check
  `stop_reason` before the text: anything but `end_turn` (a refusal that survived the fallback,
  `max_tokens`) is a failed extraction.
- **The server-side refusal fallback is on**: beta `server-side-fallback-2026-07-01`,
  `fallbacks: "default"`.
- **Its own budget throws `TimeoutException`**, which Scraping reads as `LlmTimeout`. The Model
  Server path keeps `RecipeScraping:LlmTimeoutSeconds` (120).
- **The SDK sends the key as `Authorization: Bearer`**, and an explicit key stops it reading
  `ANTHROPIC_API_KEY` or a profile from the environment.
- **CI never calls the API.** Tests script answers through `StubHttpClientFactory`
  (`AnthropicSse` builds the SSE and error bodies).

## Unavailable versus failed

**An unavailable provider is not a failed recipe.** Callers let `LlmUnavailableException` through
every per-item catch: the seed stages stop the run without recording a failure or counting toward
`MaxConsecutiveLlmFailures`, and Scraping answers 503 rather than 422.

| Outcome | State or exception | Scraping answers |
|---|---|---|
| Model Server: no HTTP answer (refused, or dropped when stopped mid-generation) / 401 / 503 | `offline` / `unauthorized` / `loading` | 503 |
| Claude: no key, 404 (unknown model) | `notConfigured` | 503 |
| Claude: 401, 403 | `unauthorized` | 503 |
| Claude: 429, 529 (after the SDK's retries) | `busy` | 503 |
| Claude: other 5xx, no connection | `offline` | 503 |
| Claude: 400 | `LlmRequestRejectedException` | 500, logged |
| Claude: `stop_reason` other than `end_turn`; invalid JSON from either | failed extraction | 422 |

**Readiness never depends on the LLM provider.** The updater rolls back on a failed
`/health/ready`, and the Model Server is off most of the time. Check `GET /api/v1/llm/status`
instead.

## Rules learned the hard way

- **Model choice dominates prompt tuning.** Moving from Qwen2.5 7B to Qwen3.5 9B took Stage 4
  from 85.7% to 96.8% and made it faster. If quality regresses, suspect the model file before
  the prompt. Claude was **not** benchmarked against the local model (ADR 0004).
- **`JsonSchemaGrammar` omits non-required properties from the grammar entirely.** An optional
  property is one the Model Server *cannot emit*, not one it may skip. Make a field required and
  nullable (`["string","null"]`) when the model should be able to say "none". Claude's structured
  outputs do allow an optional property, so the two providers differ here.
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
