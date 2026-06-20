# Phase 8 — Self-Hosted Local LLM

**Version:** 1.0  
**Date:** 2026-06-20  
**Status:** Draft  
**Depends on:** Phase 3 (Recipe Scraping) complete

> Phase 8 replaces the paid Anthropic cloud dependency with a **self-hosted model running
> in-process on a consumer NVIDIA GPU**, behind a clean **LLM abstraction layer** so the
> runtime/provider can be swapped freely later. It also extends LLM use to two further spots:
> **semantic ingredient matching** for scraped recipes (today an exact-name lookup that silently
> duplicates synonyms) and a one-off **ingredient-catalogue fill-out** command. Backend-only — no
> frontend changes.

---

## 1. Overview

Today the **only** live LLM call is recipe scraping (Phase 3), hardwired to the paid Anthropic
cloud API in `RecipeScrapeService.CallClaudeAsync`
([RecipeScrapeService.cs:324-381](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L324-L381)).
Anthropic was only the easy starting point; it is **not a requirement**. Phase 8 delivers four
workstreams:

1. **LLM abstraction layer** — a provider-agnostic `ILlmStructuredClient` seam. Callers ask for a
   JSON object conforming to a schema; they know nothing about the runtime.
2. **Local in-process provider** — a `LLamaSharp` implementation (`.NET` bindings over `llama.cpp`)
   that loads a GGUF model into the API process, runs on the GPU, and uses **GBNF grammar–constrained
   decoding** to guarantee schema-valid JSON. The `Anthropic.SDK` dependency is **removed**.
3. **Semantic ingredient matching** — after the exact-name pass, an LLM call maps remaining scraped
   ingredients to existing catalogue entries (e.g. "spring onion" → "scallion"), and supplies a
   category/unit suggestion for genuinely new ones — folding matching and classification into one call.
4. **Catalogue fill-out command** — a one-off, idempotent CLI command that generates a large
   categorised ingredient catalogue via the local model, kept out of the normal startup path.

> **What already exists (verified, do not rebuild):** the scrape fetch/strip/normalise/confirm
> pipeline and DTOs ([RecipeScrapeService.cs](../backend/RecipeApp.API/Services/RecipeScrapeService.cs)),
> the imperial→metric `ConvertUnit` and keyword `CategoriseIngredient` helpers
> ([RecipeScrapeService.cs:492-511](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L492-L511)),
> the `IngredientCategory` constants + `IsValid`
> ([IngredientCategory.cs](../backend/RecipeApp.API/Enums/IngredientCategory.cs)), the
> `Ingredient.DefaultUnit` column, and the `DataSeeder` baseline
> ([DataSeeder.cs](../backend/RecipeApp.API/Data/DataSeeder.cs)). The endpoint and service tests
> already fake at the `IRecipeScrapeService` seam
> ([RecipeScrapeEndpointsTests.cs:21-32](../backend/RecipeApp.Tests/Endpoints/RecipeScrapeEndpointsTests.cs#L21-L32)).

> **No data model changes / no migration.** Matching and fill-out reuse existing columns
> (`Ingredient.Name`, `DisplayName`, `Category`, `DefaultUnit`).

---

## 2. Deliverable

> Configure a local GGUF model path → start the API (model loads onto the GPU) → paste a recipe URL
> → the **local model** extracts the recipe with guaranteed-valid JSON → ingredients are matched
> semantically against the catalogue so synonyms reuse existing rows instead of duplicating → the
> user reviews/edits the preview and saves, exactly as before. Separately, run
> `dotnet run -- seed-catalogue 200` once to populate the catalogue with a few hundred categorised
> ingredients. No cloud API key, no network egress for inference.

---

## 3. Architecture — the LLM abstraction

New folder: `backend/RecipeApp.API/Services/Llm/`.

### 3.1 `ILlmStructuredClient`

The single seam every LLM caller depends on. Given a system prompt, user content, and a JSON schema,
it returns a JSON object that conforms to that schema.

```csharp
namespace RecipeApp.API.Services.Llm;

using System.Text.Json.Nodes;

public interface ILlmStructuredClient
{
    /// <summary>
    /// Runs a single structured completion and returns a JSON object that conforms to
    /// <paramref name="jsonSchema"/>. Implementations must guarantee schema validity
    /// (e.g. grammar-constrained decoding) — callers may deserialize the result directly.
    /// </summary>
    Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt,
        string userContent,
        JsonNode jsonSchema,
        int maxTokens,
        CancellationToken ct = default);
}
```

> The interface is deliberately runtime-free: no tool names, no model IDs, no endpoints. A future
> HTTP impl (OpenAI-compatible, e.g. `llama-server`/`llamafile`/`vLLM`/cloud) maps the schema to a
> `response_format`/tool internally; the local impl maps it to a GBNF grammar. Callers never change.

### 3.2 `LLamaSharpStructuredClient` (the Phase 8 impl)

Wraps a **single, shared** loaded model and serializes inference.

```csharp
namespace RecipeApp.API.Services.Llm;

using System.Text.Json;
using System.Text.Json.Nodes;
using LLama;
using LLama.Common;
using LLama.Sampling;
using Microsoft.Extensions.Options;

public sealed class LLamaSharpStructuredClient(LlamaModelHolder holder) : ILlmStructuredClient
{
    public async Task<JsonNode> CompleteStructuredAsync(
        string systemPrompt, string userContent, JsonNode jsonSchema, int maxTokens,
        CancellationToken ct = default)
    {
        // Build a GBNF grammar from the schema so sampling can only emit schema-valid JSON (§5).
        var gbnf = JsonSchemaGrammar.ToGbnf(jsonSchema);

        // A llama.cpp context is NOT safe for concurrent decode — serialize (single-user app).
        await holder.Gate.WaitAsync(ct);
        try
        {
            var executor = new StatelessExecutor(holder.Weights, holder.Parameters);
            var prompt   = holder.BuildChatPrompt(systemPrompt, userContent);

            var inference = new InferenceParams
            {
                MaxTokens        = maxTokens,
                SamplingPipeline = new DefaultSamplingPipeline { Grammar = new Grammar(gbnf, "root") },
                AntiPrompts      = holder.StopStrings,
            };

            var sb = new System.Text.StringBuilder();
            await foreach (var token in executor.InferAsync(prompt, inference, ct))
                sb.Append(token);

            return JsonNode.Parse(sb.ToString())
                ?? throw new InvalidOperationException("Model returned empty/invalid JSON.");
        }
        finally
        {
            holder.Gate.Release();
        }
    }
}
```

> **API-version caveat:** the exact `LLamaSharp` sampling/grammar surface (`DefaultSamplingPipeline`,
> `Grammar`, `StatelessExecutor`) shifts between releases. Pin the `LLamaSharp` package version at
> implementation time and confirm the grammar attachment API against that version; the shape above is
> representative, not API-frozen.

### 3.3 `LlamaModelHolder` — singleton model lifecycle

The model weights are **expensive** (seconds to load, gigabytes of VRAM). Load **once** and reuse.

```csharp
namespace RecipeApp.API.Services.Llm;

using LLama;
using LLama.Common;
using Microsoft.Extensions.Options;

/// <summary>Owns the single loaded model + a concurrency gate. Registered as a singleton.</summary>
public sealed class LlamaModelHolder : IDisposable
{
    public LLamaWeights Weights { get; }
    public ModelParams Parameters { get; }
    public SemaphoreSlim Gate { get; } = new(1, 1);
    public string[] StopStrings { get; }

    public LlamaModelHolder(IOptions<LlmOptions> options)
    {
        var local = options.Value.Local;
        Parameters = new ModelParams(local.ModelPath)
        {
            ContextSize   = (uint)local.ContextSize,
            GpuLayerCount = local.GpuLayerCount,   // -ngl: how many layers to offload to the GPU
        };
        Weights     = LLamaWeights.LoadFromFile(Parameters);
        StopStrings = local.StopStrings;
    }

    // Wrap system + user content in the model's chat template (model-family specific).
    public string BuildChatPrompt(string system, string user) => /* template per chosen model */;

    public void Dispose() { Weights.Dispose(); Gate.Dispose(); }
}
```

### 3.4 DI registration & selection (`Program.cs`)

```csharp
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));

// Provider selection — extensible; "Local" is the only impl shipped in Phase 8.
builder.Services.AddSingleton<LlamaModelHolder>();          // model loaded once, eagerly at startup
builder.Services.AddSingleton<ILlmStructuredClient>(sp =>
    sp.GetRequiredService<IOptions<LlmOptions>>().Value.Provider switch
    {
        "Local" => new LLamaSharpStructuredClient(sp.GetRequiredService<LlamaModelHolder>()),
        var p    => throw new InvalidOperationException($"Unknown Llm:Provider '{p}'."),
    });
```

`RecipeScrapeService` and the new ingredient services take a constructor `ILlmStructuredClient`
dependency. (`RecipeScrapeService` is currently `Scoped`; the LLM client is a `Singleton` — fine, a
scoped service may depend on a singleton.)

> **Lifecycle consequence:** the API process is now **GPU-bound** and its startup includes a one-time
> model load. Eager singleton load surfaces a bad `ModelPath`/VRAM problem at boot rather than on the
> first scrape. Expose model readiness via the existing health checks (§9).

---

## 4. NuGet changes

Edit `backend/RecipeApp.API/RecipeApp.API.csproj`:

| Package | Action | Notes |
|---|---|---|
| `LLamaSharp` | **add** | .NET bindings over `llama.cpp`. Pin an explicit version. |
| `LLamaSharp.Backend.Cuda12` | **add** | Native CUDA 12 backend. **Must match the host CUDA/driver** — the fiddly part of in-process; pin and verify with `nvidia-smi`. (Use `...Cuda11` if the box is CUDA 11.) |
| `Anthropic.SDK` | **remove** | No longer used; the scrape path no longer calls the cloud. |

> Update `CLAUDE.md`: drop the `Anthropic.SDK` row from the technology stack table and the Phase 3
> note's `Anthropic.SDK` integration mention; add the `LLamaSharp` stack entry.

---

## 5. Structured output via GBNF grammar

Anthropic guaranteed structure via *forced tool use*. Locally, we guarantee it with **grammar-
constrained sampling**: the model can only emit tokens permitted by a grammar derived from the schema.

- **Helper:** `Services/Llm/JsonSchemaGrammar.cs` — `static string ToGbnf(JsonNode jsonSchema)`
  converts the JSON-schema subset we use (objects, arrays, `string`/`integer`/`number`/`boolean`,
  nullable unions `["string","null"]`, `required`) into a GBNF grammar string. This mirrors
  `llama.cpp`'s `json_schema_to_grammar`; port that algorithm (it is small) so the **schema remains
  the single source of truth** and all three call sites reuse one converter.
- **Schemas are reused as-is:** the existing Phase 3 `extract_recipe` schema
  ([RecipeScrapeService.cs:29-101](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L29-L101))
  is kept verbatim and fed through `ToGbnf`. The two new schemas (§7, §8) are defined the same way.
- **Prompting change:** since there is no tool call, the system prompt instructs **"respond with a
  single JSON object conforming to the schema and nothing else."** The grammar enforces it; the prompt
  improves semantic quality. Reuse the existing extraction `SystemPrompt`
  ([RecipeScrapeService.cs:103-107](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L103-L107))
  with that one-line adjustment.

---

## 6. Configuration

### 6.1 New options class

Create `Services/Llm/LlmOptions.cs`:

```csharp
namespace RecipeApp.API.Services.Llm;

public class LlmOptions
{
    public const string SectionName = "Llm";

    public string Provider { get; init; } = "Local";
    public LocalLlmOptions Local { get; init; } = new();
}

public class LocalLlmOptions
{
    public string ModelPath { get; init; } = string.Empty;     // path to the .gguf file
    public int    ContextSize { get; init; } = 8192;
    public int    GpuLayerCount { get; init; } = 999;          // 999 = offload all layers to GPU
    public string? EmbeddingModelPath { get; init; }           // reserved; not used in Phase 8
    public string[] StopStrings { get; init; } = [];           // chat-template stop sequence(s)
}
```

### 6.2 `appsettings.json`

```json
"Llm": {
  "Provider": "Local",
  "Local": {
    "ModelPath": "",
    "ContextSize": 8192,
    "GpuLayerCount": 999,
    "StopStrings": []
  }
}
```

| Key | Description | Default |
|---|---|---|
| `Provider` | LLM provider selector (`Local` is the only impl in Phase 8) | `Local` |
| `Local:ModelPath` | Absolute/volume path to the GGUF model file (gitignored asset, never committed) | _empty_ |
| `Local:ContextSize` | Context window (tokens). Larger = more VRAM for KV cache | `8192` |
| `Local:GpuLayerCount` | Layers offloaded to GPU (`-ngl`); `999` = all | `999` |
| `Local:StopStrings` | Stop sequence(s) for the chosen model's chat template | _empty_ |

> Set `ModelPath` in `appsettings.Development.json` (gitignored) or an env var. The model file lives
> outside the repo.

### 6.3 `RecipeScrapingOptions` changes

Edit [RecipeScrapingOptions.cs](../backend/RecipeApp.API/Services/RecipeScrapingOptions.cs):
**remove** `AnthropicApiKey` and `Model`; **rename** `ClaudeTimeoutSeconds` → `LlmTimeoutSeconds`
(in-process inference is slower than the cloud — default it higher, e.g. `120`). Consider lowering
`MaxHtmlCharacters` for a 7–8B model (less input = faster, fewer misses). Update the matching keys in
`appsettings.json` and the timeout/cancellation usage in `RecipeScrapeService`.

---

## 7. Use case 1 — Scraping refactor

In [RecipeScrapeService.cs](../backend/RecipeApp.API/Services/RecipeScrapeService.cs):

1. Inject `ILlmStructuredClient` via the constructor; delete the `AnthropicClient` construction,
   `MessageParameters`/`Function`/`ToolUseContent` usage, and the `Anthropic.SDK` `using`s.
2. Replace the body of `CallClaudeAsync` (rename → `ExtractRecipeAsync`) with:

   ```csharp
   var schema    = JsonNode.Parse(RecipeSchemaJson)!;   // existing ToolSchemaJson, renamed
   var json      = await llm.CompleteStructuredAsync(SystemPrompt, userMessage, schema, maxTokens: 4096, ct);
   var extracted = json.Deserialize<ExtractedRecipe>(JsonOptions)
       ?? throw new RecipeScrapeException(RecipeScrapeError.NoContent, "No recipe content …");
   ```

3. Keep the `MaxHtmlCharacters` truncation, `LlmTimeoutSeconds` cancellation, and the
   `ClaudeExtractedRecipe`/`ClaudeIngredient`/`ClaudeStep` records (rename `Claude*` → neutral names
   like `Extracted*`). All of `FetchAndStripHtmlAsync`, `StripHtmlAsync`, `ConvertUnit`,
   `CategoriseIngredient`, `NormaliseAsync`, and `ConfirmAsync` are unchanged except for the matching
   addition in §8.
4. Map a timeout/inference failure to the existing `RecipeScrapeError.ClaudeTimeout`/`ClaudeFailed`
   (rename to `LlmTimeout`/`LlmFailed`) so the endpoint's `422` mapping
   ([RecipeScrapeEndpoints.cs:35-45](../backend/RecipeApp.API/Endpoints/RecipeScrapeEndpoints.cs#L35-L45))
   is preserved.

---

## 8. Use case 2 — Semantic ingredient matching + classification

Today `NormaliseAsync` matches scraped names by **exact** case-insensitive lookup
([RecipeScrapeService.cs:411](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L411)); any
synonym falls through to "new" and creates a duplicate catalogue row on confirm. Phase 8 adds a
**semantic** second pass.

**Flow inside `NormaliseAsync`:**

1. Exact-match pass unchanged (cheap; resolves the common case with zero LLM cost).
2. Collect the **unmatched** scraped ingredients. If none, skip the LLM call entirely.
3. Load the catalogue candidates (`Id`, `DisplayName`, `Category`) and assign each a **0-based
   index** for the prompt. **The model returns an index, never a GUID** — guards against hallucinated
   IDs and keeps the grammar to integers.
4. One batched `ILlmStructuredClient.CompleteStructuredAsync` call with this schema:

   ```json
   {
     "type": "object",
     "properties": {
       "results": {
         "type": "array",
         "items": {
           "type": "object",
           "properties": {
             "scraped_index":      { "type": "integer", "description": "0-based index into the unmatched list." },
             "candidate_index":    { "type": ["integer", "null"], "description": "0-based index of the matching catalogue ingredient, or null if none." },
             "confidence":         { "type": "number", "description": "0.0–1.0 match confidence." },
             "suggested_category": { "type": "string", "description": "One of the allowed categories; used only when candidate_index is null." },
             "suggested_default_unit": { "type": ["string", "null"], "description": "Suggested metric default unit for a new ingredient." }
           },
           "required": ["scraped_index", "candidate_index", "confidence", "suggested_category"]
         }
       }
     },
     "required": ["results"]
   }
   ```

   The user message lists the unmatched scraped names and the indexed candidate catalogue; the system
   prompt enumerates the allowed `IngredientCategory.All` values and instructs conservative matching.

5. Apply results:
   - `candidate_index != null` **and** `confidence >= MatchConfidenceThreshold` (config, default `0.8`)
     → resolve to that candidate's `Id`, emit as **existing** (`IsNew = false`). This is what stops
     synonym duplication.
   - Otherwise → **new** (`IsNew = true`) with `SuggestedCategory` = the model's `suggested_category`
     **if** `IngredientCategory.IsValid`, else the existing keyword `CategoriseIngredient` fallback.
     Populate the new ingredient's default unit from `suggested_default_unit` when present.
6. The preview DTO shape (`ScrapePreviewIngredient`) is unchanged — the user still reviews matches and
   can override, so a wrong auto-match is correctable before save.

> **Safety:** matching only ever changes `IngredientId`/`IsNew`/`SuggestedCategory` in the **preview**;
> nothing is persisted until the existing confirm endpoint runs. A conservative threshold plus the
> human review step bound the blast radius of a bad match.
>
> **Future scale (not now):** if the catalogue grows past comfortably fitting in a prompt, switch to a
> local embedding model + `pgvector` nearest-neighbour. `LocalLlmOptions.EmbeddingModelPath` is
> reserved for this.

---

## 9. Use case 3 — Catalogue fill-out command

A one-off, idempotent command — **outside** normal startup and separate from `DataSeeder`.

**Invocation:** `dotnet run --project backend/RecipeApp.API -- seed-catalogue [count]` (default count
e.g. `200`).

**Wiring (`Program.cs`, before `app.Run()`):** detect the `seed-catalogue` arg, build the host,
resolve `ILlmStructuredClient` + `AppDbContext` from a scope, run the importer, and **exit without
starting the web server**.

**`Services/IngredientCatalogueSeeder.cs` behaviour:**

1. Prompt the local model (structured output) for ingredients, in **batches** (e.g. 50/call) to stay
   within context and let each batch stream — schema:

   ```json
   {
     "type": "object",
     "properties": {
       "ingredients": {
         "type": "array",
         "items": {
           "type": "object",
           "properties": {
             "name":         { "type": "string", "description": "lowercase, singular, normalised" },
             "display_name": { "type": "string" },
             "category":     { "type": "string", "description": "one of the allowed categories" },
             "default_unit": { "type": "string", "description": "metric: g, kg, ml, L, pcs, tsp, tbsp" }
           },
           "required": ["name", "display_name", "category", "default_unit"]
         }
       }
     },
     "required": ["ingredients"]
   }
   ```

   The prompt passes the allowed `IngredientCategory.All` values and the allowed metric units, and asks
   for common household cooking ingredients, avoiding items already produced in earlier batches (pass
   the running name list back in).

2. For each generated item: normalise `name` to lowercase/trimmed; **validate** `category` with
   `IngredientCategory.IsValid` (skip/repair invalid); coerce `default_unit` to the allowed set.
3. **Idempotent insert:** dedupe on normalised `Name` against the DB and within the run; insert only
   missing rows; never update/clobber existing curated rows. Reuse the `ResolveOrCreateIngredient`
   pattern ([RecipeScrapeService.cs:461-490](../backend/RecipeApp.API/Services/RecipeScrapeService.cs#L461-L490)).
4. Log a summary (`requested / generated / inserted / skipped`). Re-running adds only newly-generated
   names; a second identical run inserts nothing.

---

## 10. Hardware & model guidance (~12 GB VRAM)

- **Model:** a **7–8B instruct** GGUF at Q4_K_M / Q5_K_M — e.g. **Qwen2.5-7B-Instruct** (strong at
  structured extraction) or **Llama 3.1 8B Instruct**. ~4.5–5.5 GB weights; comfortably fits 12 GB with
  full GPU offload and an 8k context. A 14B at Q4 is **tight** on 12 GB — test before committing.
- **Quality expectation:** extraction is *usable, not Sonnet-equal* — expect occasional missed
  ingredients / imperfect step↔ingredient indexes; the existing **human review step**
  (`ScrapePreviewResponse` → edit → confirm) is what makes this acceptable. Matching and classification
  are near-trivial for this class of model. Latency is acceptable (personal use); a smaller/faster
  model can be substituted by changing `ModelPath`.
- **`GpuLayerCount`:** start at `999` (all layers on GPU). If VRAM is tight at the chosen context size,
  lower it to spill some layers to CPU (slower but fits).

---

## 11. Data flow

```
Startup
  → API process loads GGUF onto GPU (LlamaModelHolder singleton) — fails fast on bad path/VRAM

Scrape (POST /api/v1/recipes/scrape)
  → fetch + strip HTML (unchanged)
  → ILlmStructuredClient.CompleteStructuredAsync(extract_recipe schema → GBNF grammar)
      → LLamaSharp StatelessExecutor, grammar-constrained → guaranteed-valid recipe JSON
  → NormaliseAsync:
      → exact-name match pass (unchanged)
      → unmatched? one batched LLM match+classify call (candidate indexes → IDs / new + category)
      → imperial→metric ConvertUnit (unchanged)
  → ScrapePreviewResponse (synonyms now reuse existing rows)
  → user reviews/edits → POST .../confirm → persists (unchanged)

Catalogue fill-out (dotnet run -- seed-catalogue [count])
  → batched LLM generation → validate category/unit → idempotent insert by normalised Name → exit
```

---

## 12. Testing

### 12.1 Existing tests — keep green

- Endpoint tests fake `IRecipeScrapeService`
  ([RecipeScrapeEndpointsTests.cs:21-32](../backend/RecipeApp.Tests/Endpoints/RecipeScrapeEndpointsTests.cs#L21-L32))
  → unaffected.
- `RecipeScrapeServiceTests` covers only pure helpers (`StripHtmlAsync`, `ConvertUnit`,
  `CategoriseIngredient`) → unaffected. Update any `RecipeScrapeError`/method renames.

### 12.2 New backend tests

Tests must **never load a real model** — inject a fake `ILlmStructuredClient`.

```
backend/RecipeApp.Tests/
├── Services/Llm/
│   ├── JsonSchemaGrammarTests.cs       schema→GBNF for object/array/nullable/required; round-trips a sample
│   └── FakeLlmStructuredClient.cs      returns a canned JsonNode per call (no model)
├── Services/
│   ├── RecipeScrapeServiceMatchingTests.cs   exact-match short-circuit; synonym → existing ID at/above
│   │                                          threshold; below threshold → new + valid category;
│   │                                          invalid category → keyword fallback; candidate_index→GUID map
│   └── IngredientCatalogueSeederTests.cs      idempotency (run twice → 0 inserted 2nd time);
│                                              invalid category skipped; dedupe by normalised name
```

- **`JsonSchemaGrammar`:** asserts the converter emits valid GBNF for each of the three schemas and
  that a schema-valid sample parses; a structurally-invalid sample is rejected by the grammar.
- **Matching:** drive `NormaliseAsync` with a `FakeLlmStructuredClient` returning crafted `results`;
  assert IDs/`IsNew`/category outcomes and that **no GUIDs originate from the model** (index→ID map).
- **Catalogue seeder:** fake client returns a fixed ingredient batch; assert idempotent inserts,
  category validation, and unit coercion against a Testcontainers Postgres (existing `DatabaseFixture`).

### 12.3 Manual, end-to-end (in-process)

1. Place a 7–8B instruct GGUF on disk; set `Llm:Local:ModelPath`, `GpuLayerCount`, `ContextSize`.
2. `dotnet run` → confirm the model loads at startup and GPU VRAM is in use (`nvidia-smi`); health
   readiness reports the model loaded (§13).
3. `POST /api/v1/recipes/scrape` with a real recipe URL → sensible preview; output is always valid JSON.
4. Scrape a recipe using a synonym ("spring onion" when "scallion" exists) → it **matches** the
   existing row rather than creating a duplicate.
5. `dotnet run -- seed-catalogue 200` → rows added; **re-run → none added**.
6. `dotnet test` (Testcontainers Postgres required for DB-backed tests).

---

## 13. Docker / ops

- **API container becomes GPU-bound.** In `docker-compose.yml`, give the API service GPU access
  (NVIDIA Container Toolkit / `deploy.resources.reservations.devices` reserving `gpu`) and mount a
  **model volume** holding the GGUF; point `Llm:Local:ModelPath` at the mounted path. There is **no
  separate inference service** — the model runs inside this process. `nginx.conf` is untouched.
- **Health:** extend the readiness check
  ([Program.cs:53-58](../backend/RecipeApp.API/Program.cs#L53-L58)) with a check that the
  `LlamaModelHolder` weights are loaded, so `/health/ready` reflects model availability.
- **Startup time:** the eager model load adds seconds to boot; acceptable for a long-lived process.

---

## 14. Out of scope for Phase 8

- **Keeping/maintaining the Anthropic (or any cloud) provider** — the `ILlmStructuredClient` seam makes
  an HTTP/OpenAI-compatible or cloud impl a drop-in later, but none is built in Phase 8.
- **Embeddings / `pgvector`** ingredient matching — reserved (`EmbeddingModelPath`); Phase 8 matches
  in-prompt.
- **Multi-GPU / concurrent decode / batching** — single serialized executor (single-user app).
- **Streaming scrape results to the UI**, recipe-from-image, fine-tuning, or model auto-download.
- **Frontend changes** — none; provider is server config and the preview already renders matched-vs-new
  ingredients and `SuggestedCategory`.

---

## 15. Definition of Done

- [ ] **Abstraction:** `ILlmStructuredClient` added; all LLM callers depend only on it.
- [ ] **Local provider:** `LLamaSharpStructuredClient` + `LlamaModelHolder` (singleton, eager load,
      `SemaphoreSlim`-serialized) + `JsonSchemaGrammar` (schema→GBNF) implemented; output is always
      schema-valid JSON.
- [ ] **NuGet:** `LLamaSharp` + matching CUDA backend added and version-pinned; `Anthropic.SDK`
      removed; `CLAUDE.md` stack/notes updated.
- [ ] **Config:** `LlmOptions`/`LocalLlmOptions` added; `appsettings.json` `Llm` section added;
      `RecipeScrapingOptions` cloud keys removed and `LlmTimeoutSeconds` renamed/raised.
- [ ] **Scraping:** `RecipeScrapeService` uses the abstraction; no `Anthropic.SDK` references remain;
      `422` error mapping preserved.
- [ ] **Matching:** `NormaliseAsync` resolves synonyms to existing catalogue rows above the confidence
      threshold (no duplicate creation); new ingredients get a validated category/unit; the model
      returns candidate **indexes**, not GUIDs.
- [ ] **Catalogue command:** `dotnet run -- seed-catalogue [count]` generates, validates, and
      idempotently inserts ingredients without touching `DataSeeder`; re-runs insert nothing.
- [ ] **Docker/ops:** API service has GPU access + model volume; `/health/ready` reflects model load.
- [ ] **Tests:** new tests pass with a fake `ILlmStructuredClient` (no model loaded); existing scrape
      tests green; full backend suite green.
- [ ] **Manual E2E:** local model scrape, synonym-match, and idempotent catalogue fill-out verified.
- [ ] `CLAUDE.md` "Current phase" line updated to **Phase 8** with structure notes.
