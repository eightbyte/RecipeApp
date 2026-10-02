# Llm — local structured-output layer

LLamaSharp 0.27.0 with the CUDA12 backend (`LLamaSharp` + `LLamaSharp.Backend.Cuda12`). No cloud
calls. Used by recipe scraping (`RecipeScrapeService`) and the seed pipeline's LLM stages.

| Type | Role |
|---|---|
| `ILlmStructuredClient` | The abstraction consumers take. Tests use `StubLlmStructuredClient` |
| `LLamaSharpStructuredClient` | GBNF grammar-constrained decoding against a JSON schema |
| `JsonSchemaGrammar` | JSON Schema → GBNF converter (supports `enum`, `["type","null"]`) |
| `LlamaModelHolder` | Singleton that owns the weights plus a `SemaphoreSlim` gate. **Loads the model in its constructor** |
| `DeferredLlmStructuredClient` | Builds the real client on first completion, so paths that never infer (Stage 5) never load the model. The web host still resolves the holder eagerly at startup |
| `LlmOptions` / `LlmSamplingOptions` | Bound to `Llm:*` |

## Configuration (`Llm:Local:*`)

- `ModelPath` is an absolute GGUF path, currently **Qwen3.5-9B-Q6_K**. An empty value runs
  without a model (scraping disabled).
- `GpuLayerCount` defaults to 999 (all layers). Set 0 for CPU only.
- `ChatTemplate` is `chatml` (Qwen, Mistral), `llama3` or `gemma`. **Unrecognised values silently
  fall through to ChatML.**
- `Sampling:*` holds LLamaSharp's own defaults. **This is a measured result, not an oversight:**
  colder decoding made extraction worse, because retries stopped recovering reproducible misreads.

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
