---
status: accepted
date: 2026-11-14
issue: "#69"
supersedes: "0002, in part"
---

# The LLM provider for Scraping is chosen per deployment, and may be the Claude API

ADR 0002 moved inference into a local Model Server and ruled out cloud calls. Scraping then works
only while someone has started the Model Server on a machine with a GPU. We make Scraping's
provider a per-deployment setting, `Llm:Provider`: `ModelServer` (the default) or `Anthropic`,
the Claude API. The seed pipeline keeps using the Model Server.

## Decision

- **One provider per install, set by configuration.** `Llm:Provider` in `appsettings*.json`, or
  `LLM_PROVIDER` in production's `.env`. It changes on restart, never at runtime, and there is no
  per-user choice and no automatic fallback between providers.
- **Only Scraping follows it.** Both clients are registered, keyed by provider. Scraping (from a
  URL and from pasted text) takes the configured one. `seed-recipes` and
  `IngredientCatalogueBuilder` ask for the Model Server by key, because their prompts and quality
  gates were measured against it (ADR 0002's benchmark).
- **Claude uses structured outputs, not tool use.** `output_config.format` carries the caller's
  JSON schema, fitted to the subset the API accepts: every object closed with
  `additionalProperties: false`, and `["type","null"]` written as `anyOf`. Forced `tool_choice`
  returns a 400 on the default model anyway.
- **Request shape.** The official `Anthropic` NuGet package. Model `claude-opus-5-5`, effort `low`,
  no `thinking` setting (thinking can't be turned off on that model), and no sampling parameters
  (current models reject them). `ThinkingHeadroomTokens` (8000) is added to the caller's
  `max_tokens`, because thinking counts against it. The request is streamed and the final message
  read. The server-side refusal fallback is on (`fallbacks: "default"`). Claude has its own
  60-second budget, retries included. The Model Server keeps Scraping's 120 seconds.
- **Failures keep ADR 0002's split between "unavailable" and "failed".** A rejected key (401/403),
  an unknown model (404), rate limits and overload (429/529), other 5xx and no connection mean the
  provider can't serve, so Scraping answers 503. A refusal that survives the fallback, or an answer
  cut off at `max_tokens`, is a failed extraction (422). A 400 is a bug in the request, logged
  and answered with 500.
- **The status endpoint names the provider.** `GET /api/v1/llm/status` replaces
  `/api/v1/model-server/status` and returns `{ provider, state, model }`. Two states are new:
  `busy` (429/529) and `notConfigured` (no key, or a model the key can't use). The Claude probe
  asks the Models API for the configured model. `/health/ready` still never depends on either
  provider.
- **Users are told when content leaves the network.** With a cloud provider, the Import Recipe
  sheet shows "Extracted with Claude (Anthropic)".
- **Keys stay out of the repository.** Development uses `dotnet user-secrets`
  (`Llm:Anthropic:ApiKey`) or the git-ignored `appsettings.Development.json`. Production uses
  `ANTHROPIC_API_KEY` in `.env`. CI tests against a stubbed HTTP handler, with no key and no live
  call.

## Considered options

**A. Claude for everything, seed pipeline included.** *Rejected:* the seed library is complete,
and its gates were tuned against the local model. Changing engines there means a new benchmark
for no new recipes.

**B. Tool use with a forced `tool_choice`.** *Rejected:* forced tool choice returns a 400 on the
default model, and structured outputs give the same guarantee without a tool.

**C. Automatic fallback from the Model Server to Claude.** *Rejected for now:* content would leave
the network silently whenever the local server happened to be off, which is a privacy decision a
user should make, not a side effect of a stopped process.

## Consequences

- **The choice was not benchmarked.** No Stage-4-style measurement compares Claude's extractions
  with the local model's. Tuning (effort, model, prompt caching) is deferred until there is a
  reason and an eval to measure it against.
- A deployment on Claude sends each Scrape's page text, or the pasted text, to Anthropic, and pays
  per request.
- `Llm:Sampling:*` moved to `Llm:ModelServer:Sampling:*`, since only the Model Server uses it.
  Production's `.env` never overrode it.
- ADR 0002 still holds for the Model Server and the seed pipeline. Its "no cloud calls" and its
  option E (removing `Llm:Provider`) are superseded here.
