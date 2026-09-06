using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.Services;

public class RecipeScrapeService(
    IHttpClientFactory httpClientFactory,
    IOptions<RecipeScrapingOptions> options,
    ILlmStructuredClient llm,
    AppDbContext db,
    IHeadlessRenderer headlessRenderer,
    ILogger<RecipeScrapeService> logger) : IRecipeScrapeService
{
    private readonly RecipeScrapingOptions _options = options.Value;

    // ── Recipe extraction schema ──────────────────────────────────────────────

    private const string RecipeSchemaJson = """
        {
          "type": "object",
          "properties": {
            "name": {
              "type": "string",
              "description": "The recipe name."
            },
            "description": {
              "type": ["string", "null"],
              "description": "A short food description. Null if not present."
            },
            "servings": {
              "type": "integer",
              "description": "Number of servings the base recipe yields. Default 4 if not stated."
            },
            "ingredients": {
              "type": "array",
              "description": "All ingredients required by the recipe.",
              "items": {
                "type": "object",
                "properties": {
                  "name": {
                    "type": "string",
                    "description": "Ingredient name in lowercase, singular, normalised (e.g. 'onion', 'chicken breast')."
                  },
                  "display_name": {
                    "type": "string",
                    "description": "Ingredient name as it should be displayed (e.g. 'Onion', 'Chicken Breast')."
                  },
                  "amount": {
                    "type": "number",
                    "description": "Numeric quantity. Use the amount as stated on the page."
                  },
                  "unit": {
                    "type": "string",
                    "description": "Unit of measurement as stated on the page. Preserve the original unit; do not convert."
                  },
                  "notes": {
                    "type": ["string", "null"],
                    "description": "Preparation notes (e.g. 'finely chopped', 'optional'). Null if none."
                  }
                },
                "required": ["name", "display_name", "amount", "unit"]
              }
            },
            "steps": {
              "type": "array",
              "description": "Ordered cooking steps.",
              "items": {
                "type": "object",
                "properties": {
                  "step_number": {
                    "type": "integer",
                    "description": "1-based step number."
                  },
                  "instruction": {
                    "type": "string",
                    "description": "Full step instruction text."
                  },
                  "ingredient_indexes": {
                    "type": "array",
                    "items": { "type": "integer" },
                    "description": "0-based indexes into the ingredients array for ingredients used in this step."
                  }
                },
                "required": ["step_number", "instruction", "ingredient_indexes"]
              }
            }
          },
          "required": ["name", "servings", "ingredients", "steps"]
        }
        """;

    // Semantic matching schema for unmatched scraped ingredients
    private const string MatchingSchemaJson = """
        {
          "type": "object",
          "properties": {
            "results": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "scraped_index":          { "type": "integer" },
                  "candidate_index":        { "type": ["integer", "null"] },
                  "confidence":             { "type": "number" },
                  "suggested_category":     { "type": "string" },
                  "suggested_default_unit": { "type": ["string", "null"] }
                },
                "required": ["scraped_index", "candidate_index", "confidence", "suggested_category"]
              }
            }
          },
          "required": ["results"]
        }
        """;

    private const string ExtractionSystemPrompt =
        "You are a recipe data extraction assistant. " +
        "Extract the complete recipe from the web page text provided by the user. " +
        "If any information is missing or ambiguous, make a reasonable best-guess rather than omitting it. " +
        "Respond with a single JSON object conforming to the schema and nothing else.";

    // ── Ingredient category heuristic ─────────────────────────────────────────

    private static readonly (string Category, string[] Keywords)[] CategoryPriority =
    [
        (IngredientCategory.MeatSeafood, ["chicken", "beef", "pork", "lamb", "turkey", "duck", "bacon", "ham", "sausage", "mince", "steak", "fillet", "breast", "thigh", "salmon", "tuna", "cod", "prawn", "shrimp", "crab", "lobster", "mussel", "anchovy", "chorizo", "salami", "pepperoni"]),
        (IngredientCategory.Dairy,       ["milk", "cream", "butter", "cheese", "yogurt", "yoghurt", "egg", "parmesan", "mozzarella", "cheddar", "ricotta", "brie", "ghee", "crème fraîche", "sour cream"]),
        (IngredientCategory.Canned,      ["canned", "tinned", "kidney bean", "chickpea", "black bean", "cannellini", "coconut milk", "chopped tomato", "diced tomato", "tomato paste"]),
        (IngredientCategory.Frozen,      ["frozen"]),
        (IngredientCategory.Bakery,      ["bread", "roll", "bun", "baguette", "pita", "tortilla", "wrap", "crumpet", "croissant"]),
        (IngredientCategory.Beverages,   ["stock", "broth", "wine", "beer", "juice", "coffee", "tea"]),
        (IngredientCategory.Condiments,  ["oil", "vinegar", "sauce", "soy", "fish sauce", "worcestershire", "mustard", "ketchup", "mayonnaise", "honey", "miso", "tahini", "paprika", "cumin", "turmeric", "cinnamon", "nutmeg", "curry", "chilli flake", "cayenne", "vanilla", "extract", "seasoning", "spice", "herb"]),
        (IngredientCategory.DryGoods,    ["flour", "sugar", "salt", "rice", "pasta", "noodle", "oat", "breadcrumb", "lentil", "quinoa", "couscous", "baking powder", "baking soda", "yeast", "cornstarch", "cornflour", "cocoa", "chocolate", "almond", "walnut", "cashew", "peanut", "sesame", "seed", "nut"]),
        (IngredientCategory.Produce,     ["onion", "garlic", "carrot", "celery", "tomato", "potato", "lettuce", "spinach", "kale", "broccoli", "pepper", "capsicum", "zucchini", "cucumber", "avocado", "lemon", "lime", "orange", "apple", "banana", "mushroom", "corn", "asparagus", "pea", "parsley", "basil", "coriander", "thyme", "rosemary", "mint", "dill", "chive", "scallion", "leek", "shallot", "ginger", "chilli", "eggplant", "beetroot", "pumpkin", "squash", "berry"]),
    ];

    // ── Imperial-to-metric unit conversion ────────────────────────────────────

    private static readonly Dictionary<string, (string MetricUnit, double Factor)> UnitConversions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["oz"]           = ("g",  28.3495),
            ["ounce"]        = ("g",  28.3495),
            ["ounces"]       = ("g",  28.3495),
            ["lb"]           = ("g",  453.592),
            ["lbs"]          = ("g",  453.592),
            ["pound"]        = ("g",  453.592),
            ["pounds"]       = ("g",  453.592),
            ["fl oz"]        = ("ml", 29.5735),
            ["fluid oz"]     = ("ml", 29.5735),
            ["fluid ounce"]  = ("ml", 29.5735),
            ["cup"]          = ("ml", 240.0),
            ["cups"]         = ("ml", 240.0),
            ["pt"]           = ("ml", 473.176),
            ["pint"]         = ("ml", 473.176),
            ["pints"]        = ("ml", 473.176),
            ["qt"]           = ("ml", 946.353),
            ["quart"]        = ("ml", 946.353),
            ["quarts"]       = ("ml", 946.353),
            ["gal"]          = ("L",  3.78541),
            ["gallon"]       = ("L",  3.78541),
            ["gallons"]      = ("L",  3.78541),
        };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling              = JsonNumberHandling.AllowReadingFromString,
    };

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<ScrapePreviewResponse> ScrapeAsync(string url, CancellationToken ct = default)
    {
        var strippedText = await FetchAndStripHtmlAsync(url, ct);
        var extracted    = await ExtractRecipeAsync(strippedText, ct);
        return await NormaliseAsync(extracted, url, ct);
    }

    public async Task<RecipeDetailResponse> ConfirmAsync(ScrapeConfirmRequest request, CancellationToken ct = default)
    {
        var resolvedIngredientIds = new List<Guid>();
        foreach (var ing in request.Ingredients.OrderBy(i => i.DisplayOrder))
        {
            if (ing.IngredientId.HasValue)
            {
                resolvedIngredientIds.Add(ing.IngredientId.Value);
            }
            else
            {
                var normalised = ing.NewIngredientName!.Trim().ToLowerInvariant();
                var id = await ResolveOrCreateIngredientAsync(
                    normalised,
                    ing.NewIngredientDisplayName!.Trim(),
                    ing.Category!,
                    ct);
                resolvedIngredientIds.Add(id);
            }
        }

        var recipe = new Recipe
        {
            Id          = Guid.NewGuid(),
            Name        = request.Name.Trim(),
            Description = request.Description?.Trim(),
            SourceUrl   = request.SourceUrl.Trim(),
            Servings    = request.Servings,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };
        db.Recipes.Add(recipe);

        var recipeIngredients = new List<RecipeIngredient>();
        var orderedIngredients = request.Ingredients.OrderBy(i => i.DisplayOrder).ToList();
        for (int i = 0; i < orderedIngredients.Count; i++)
        {
            var ing = orderedIngredients[i];
            var ri = new RecipeIngredient
            {
                Id           = Guid.NewGuid(),
                RecipeId     = recipe.Id,
                IngredientId = resolvedIngredientIds[i],
                Amount       = ing.Amount,
                Unit         = ing.Unit,
                Notes        = ing.Notes?.Trim(),
                DisplayOrder = ing.DisplayOrder,
            };
            db.RecipeIngredients.Add(ri);
            recipeIngredients.Add(ri);
        }

        foreach (var stepReq in request.Steps.OrderBy(s => s.StepNumber))
        {
            var step = new RecipeStep
            {
                Id          = Guid.NewGuid(),
                RecipeId    = recipe.Id,
                StepNumber  = stepReq.StepNumber,
                Instruction = stepReq.Instruction.Trim(),
            };
            db.RecipeSteps.Add(step);

            foreach (var idx in stepReq.IngredientIndexes.Distinct())
            {
                if (idx >= 0 && idx < recipeIngredients.Count)
                    db.RecipeStepIngredients.Add(new RecipeStepIngredient
                    {
                        StepId             = step.Id,
                        RecipeIngredientId = recipeIngredients[idx].Id,
                    });
            }
        }

        await db.SaveChangesAsync(ct);

        var full = await db.Recipes
            .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Ingredients).ThenInclude(ri => ri.StepIngredients)
            .Include(r => r.Steps).ThenInclude(rs => rs.StepIngredients)
            .AsNoTracking()
            .FirstAsync(r => r.Id == recipe.Id, ct);

        return full.ToDetail();
    }

    // ── HTML fetching and stripping ───────────────────────────────────────────

    public async Task<string> FetchAndStripHtmlAsync(string url, CancellationToken ct)
    {
        var html = await FetchHtmlAsync(url, ct);
        var (text, sufficient) = await ExtractCandidateTextAsync(html);
        if (sufficient) return text;

        if (!_options.EnableHeadlessFallback) return text;

        try
        {
            var renderedHtml = await headlessRenderer.RenderAsync(
                url, TimeSpan.FromSeconds(_options.HeadlessRenderTimeoutSeconds), ct);
            var (renderedText, renderedSufficient) = await ExtractCandidateTextAsync(renderedHtml);

            // Prefer the rendered result if it cleared the bar, or is simply more substantial
            // than what the static fetch produced (e.g. JSON-LD injected only after hydration).
            if (renderedSufficient || renderedText.Length > text.Length)
                return renderedText;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Headless render fallback failed for {Url}; using static fetch result.", url);
        }

        return text;
    }

    private async Task<string> FetchHtmlAsync(string url, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("RecipeScraper");

        using var fetchCts  = new CancellationTokenSource(TimeSpan.FromSeconds(_options.HtmlFetchTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, fetchCts.Token);

        HttpResponseMessage response;
        try
        {
            response = await client.GetAsync(url, linkedCts.Token);
        }
        catch (OperationCanceledException) when (fetchCts.IsCancellationRequested)
        {
            throw new RecipeScrapeException(RecipeScrapeError.FetchTimeout,
                "The recipe page could not be retrieved within the allowed time.");
        }
        catch (HttpRequestException ex)
        {
            throw new RecipeScrapeException(RecipeScrapeError.FetchFailed,
                $"The recipe page could not be retrieved: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            if (response.Headers.TryGetValues("cf-mitigated", out _) ||
                response.Headers.Server.Any(s => s.Product?.Name == "cloudflare"))
                throw new RecipeScrapeException(RecipeScrapeError.FetchNonSuccess,
                    $"The recipe page returned HTTP {(int)response.StatusCode}. " +
                    "This site is protected by a Cloudflare bot-detection challenge that cannot be solved automatically. " +
                    "Try a different recipe source, or add the recipe manually.");

            var errorBody = await response.Content.ReadAsStringAsync(ct);
            var snippet    = errorBody.Length > 300 ? errorBody[..300] : errorBody;
            throw new RecipeScrapeException(RecipeScrapeError.FetchNonSuccess,
                $"The recipe page returned HTTP {(int)response.StatusCode}. Response: {snippet}");
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>Tries JSON-LD recipe markup first (clean, structured, and unaffected by the
    /// script-stripping below); falls back to stripped visible body text. "Sufficient" means the
    /// caller doesn't need to try harder (JSON-LD found, or stripped text clears the length bar).</summary>
    private async Task<(string Text, bool Sufficient)> ExtractCandidateTextAsync(string html)
    {
        var parser = new HtmlParser();
        var doc    = await parser.ParseDocumentAsync(html);

        var jsonLdText = RecipeJsonLdExtractor.TryBuildRecipeText(doc);
        if (jsonLdText is not null) return (jsonLdText, true);

        var strippedText = StripBodyText(doc);
        return (strippedText, strippedText.Length >= _options.MinStaticContentLength);
    }

    public static async Task<string> StripHtmlAsync(string html)
    {
        var parser = new HtmlParser();
        var doc    = await parser.ParseDocumentAsync(html);
        return StripBodyText(doc);
    }

    private static string StripBodyText(IDocument doc)
    {
        string[] removeSelectors =
        [
            "script", "style", "noscript",
            "nav", "header", "footer", "aside",
            "iframe", "form", "button", "input", "select", "textarea"
        ];

        foreach (var selector in removeSelectors)
            foreach (var el in doc.QuerySelectorAll(selector).ToArray())
                el.Remove();

        foreach (var el in doc.QuerySelectorAll("[style]").ToArray())
        {
            var style = (el.GetAttribute("style") ?? "").Replace(" ", "");
            if (style.Contains("display:none", StringComparison.OrdinalIgnoreCase) ||
                style.Contains("visibility:hidden", StringComparison.OrdinalIgnoreCase))
                el.Remove();
        }

        var text = doc.Body?.TextContent ?? string.Empty;
        text = Regex.Replace(text, @"[\r\n\t]+|\s{2,}", " ").Trim();
        return text;
    }

    // ── LLM extraction ────────────────────────────────────────────────────────

    private async Task<ExtractedRecipe> ExtractRecipeAsync(string strippedText, CancellationToken ct)
    {
        if (_options.MaxHtmlCharacters > 0 && strippedText.Length > _options.MaxHtmlCharacters)
            strippedText = strippedText[.._options.MaxHtmlCharacters];

        var userMessage = $"Extract the recipe from the following web page text:\n\n---\n{strippedText}\n---";
        var schema      = JsonNode.Parse(RecipeSchemaJson)!;

        using var llmCts    = new CancellationTokenSource(TimeSpan.FromSeconds(_options.LlmTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, llmCts.Token);

        JsonNode json;
        try
        {
            json = await llm.CompleteStructuredAsync(ExtractionSystemPrompt, userMessage, schema, maxTokens: 4096, linkedCts.Token);
        }
        catch (OperationCanceledException) when (llmCts.IsCancellationRequested)
        {
            throw new RecipeScrapeException(RecipeScrapeError.LlmTimeout,
                "Recipe extraction timed out. Please try again.");
        }
        catch (Exception ex) when (ex is not RecipeScrapeException)
        {
            throw new RecipeScrapeException(RecipeScrapeError.LlmFailed,
                "Recipe extraction failed. Please try again or create the recipe manually.");
        }

        var extracted = json.Deserialize<ExtractedRecipe>(JsonOptions)
            ?? throw new RecipeScrapeException(RecipeScrapeError.NoContent,
                "No recipe content could be extracted from this page.");

        return extracted;
    }

    // ── Normalisation with semantic matching ──────────────────────────────────

    internal async Task<ScrapePreviewResponse> NormaliseAsync(
        ExtractedRecipe extracted, string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(extracted.Name) ||
            extracted.Ingredients.Count == 0 ||
            extracted.Steps.Count == 0)
            throw new RecipeScrapeException(RecipeScrapeError.NoContent,
                "No recipe content could be extracted from this page.");

        var dbIngredients = await db.Ingredients
            .AsNoTracking()
            .Select(i => new { i.Id, i.Name, i.DisplayName, i.Category })
            .ToListAsync(ct);

        var exactLookup = dbIngredients
            .ToDictionary(i => i.Name, i => i.Id, StringComparer.OrdinalIgnoreCase);

        // Pass 1: exact match
        var previewIngredients = new List<ScrapePreviewIngredient>();
        var unmatchedIndexes   = new List<int>();

        for (int i = 0; i < extracted.Ingredients.Count; i++)
        {
            var ing            = extracted.Ingredients[i];
            var normalisedName = ing.Name.Trim().ToLowerInvariant();
            var (amount, unit) = ConvertUnit(ing.Amount, ing.Unit);

            if (exactLookup.TryGetValue(normalisedName, out var existingId))
            {
                previewIngredients.Add(new ScrapePreviewIngredient(
                    IngredientId:      existingId,
                    Name:              normalisedName,
                    DisplayName:       ing.DisplayName,
                    Amount:            amount,
                    Unit:              unit,
                    Notes:             string.IsNullOrWhiteSpace(ing.Notes) ? null : ing.Notes,
                    IsNew:             false,
                    SuggestedCategory: IngredientCategory.Other,
                    DisplayOrder:      i));
            }
            else
            {
                previewIngredients.Add(new ScrapePreviewIngredient(
                    IngredientId:      null,
                    Name:              normalisedName,
                    DisplayName:       ing.DisplayName,
                    Amount:            amount,
                    Unit:              unit,
                    Notes:             string.IsNullOrWhiteSpace(ing.Notes) ? null : ing.Notes,
                    IsNew:             true,
                    SuggestedCategory: CategoriseIngredient(normalisedName),
                    DisplayOrder:      i));
                unmatchedIndexes.Add(i);
            }
        }

        // Pass 2: semantic match for unmatched ingredients (one batched LLM call)
        if (unmatchedIndexes.Count > 0 && dbIngredients.Count > 0)
        {
            var candidates = dbIngredients
                .Select((ing, idx) => (idx, ing))
                .ToList();

            var unmatchedNames = unmatchedIndexes
                .Select((origIdx, pos) => $"{pos}: {extracted.Ingredients[origIdx].Name.Trim().ToLowerInvariant()}")
                .ToList();

            var candidateList = candidates
                .Select(c => $"{c.idx}: {c.ing.DisplayName} ({c.ing.Category})")
                .ToList();

            var categoriesStr = string.Join(", ", IngredientCategory.All);
            var systemPrompt  =
                $"You are an ingredient matching assistant. Match each scraped ingredient to the closest catalogue entry, or null if no good match exists.\n" +
                $"Be conservative — only match when confident (synonyms, alternate spellings). Do not match if the ingredient is genuinely different.\n" +
                $"Allowed categories: {categoriesStr}.\n" +
                "Respond with a single JSON object conforming to the schema and nothing else.";

            var userContent =
                $"Scraped ingredients (index: name):\n{string.Join("\n", unmatchedNames)}\n\n" +
                $"Catalogue candidates (index: name (category)):\n{string.Join("\n", candidateList)}";

            var matchSchema = JsonNode.Parse(MatchingSchemaJson)!;

            try
            {
                var matchJson = await llm.CompleteStructuredAsync(
                    systemPrompt, userContent, matchSchema, maxTokens: 2048, ct);

                var matchResponse = matchJson.Deserialize<MatchingResponse>(JsonOptions);
                if (matchResponse?.Results != null)
                {
                    foreach (var result in matchResponse.Results)
                    {
                        if (result.ScrapedIndex < 0 || result.ScrapedIndex >= unmatchedIndexes.Count)
                            continue;

                        var origIdx    = unmatchedIndexes[result.ScrapedIndex];
                        var prevIngred = previewIngredients[origIdx];

                        if (result.CandidateIndex.HasValue
                            && result.CandidateIndex.Value >= 0
                            && result.CandidateIndex.Value < candidates.Count
                            && result.Confidence >= _options.MatchConfidenceThreshold)
                        {
                            // Matched to existing catalogue entry
                            var matchedId = candidates[result.CandidateIndex.Value].ing.Id;
                            previewIngredients[origIdx] = prevIngred with
                            {
                                IngredientId      = matchedId,
                                IsNew             = false,
                                SuggestedCategory = IngredientCategory.Other,
                            };
                        }
                        else
                        {
                            // New ingredient — apply LLM-suggested category/unit
                            var suggestedCategory = IngredientCategory.IsValid(result.SuggestedCategory)
                                ? result.SuggestedCategory
                                : CategoriseIngredient(prevIngred.Name);

                            previewIngredients[origIdx] = prevIngred with
                            {
                                SuggestedCategory = suggestedCategory,
                            };
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Semantic matching is best-effort — fall back to keyword categorisation already set
            }
        }

        var previewSteps = extracted.Steps
            .OrderBy(s => s.StepNumber)
            .Select(s => new ScrapePreviewStep(s.StepNumber, s.Instruction, s.IngredientIndexes))
            .ToList();

        return new ScrapePreviewResponse(
            Name:        extracted.Name.Trim(),
            Description: string.IsNullOrWhiteSpace(extracted.Description) ? null : extracted.Description.Trim(),
            Servings:    extracted.Servings > 0 ? extracted.Servings : 4,
            SourceUrl:   url,
            Ingredients: previewIngredients,
            Steps:       previewSteps
        );
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public async Task<Guid> ResolveOrCreateIngredientAsync(
        string normalised, string displayName, string category, CancellationToken ct)
    {
        var existing = await db.Ingredients
            .FirstOrDefaultAsync(i => i.Name == normalised, ct);
        if (existing != null) return existing.Id;

        var newIng = new Ingredient
        {
            Id          = Guid.NewGuid(),
            Name        = normalised,
            DisplayName = displayName,
            Category    = category,
            CreatedAt   = DateTime.UtcNow,
        };

        try
        {
            db.Ingredients.Add(newIng);
            await db.SaveChangesAsync(ct);
            return newIng.Id;
        }
        catch (DbUpdateException)
        {
            db.Entry(newIng).State = EntityState.Detached;
            var raceExisting = await db.Ingredients.FirstAsync(i => i.Name == normalised, ct);
            return raceExisting.Id;
        }
    }

    public static string CategoriseIngredient(string normalisedName)
    {
        foreach (var (category, keywords) in CategoryPriority)
        {
            if (keywords.Any(kw => normalisedName.Contains(kw, StringComparison.OrdinalIgnoreCase)))
                return category;
        }
        return IngredientCategory.Other;
    }

    public static (decimal Amount, string Unit) ConvertUnit(double rawAmount, string unit)
    {
        var trimmed = unit.Trim();
        if (UnitConversions.TryGetValue(trimmed, out var conversion))
        {
            var converted = Math.Round((decimal)(rawAmount * conversion.Factor), 3);
            return (converted, conversion.MetricUnit);
        }
        return (Math.Round((decimal)rawAmount, 3), unit);
    }

    // ── Internal DTOs for LLM responses ──────────────────────────────────────

    internal record ExtractedRecipe(
        string Name,
        string? Description,
        int Servings,
        List<ExtractedIngredient> Ingredients,
        List<ExtractedStep> Steps
    );

    internal record ExtractedIngredient(
        string Name,
        [property: JsonPropertyName("display_name")] string DisplayName,
        double Amount,
        string Unit,
        string? Notes
    );

    internal record ExtractedStep(
        [property: JsonPropertyName("step_number")]       int StepNumber,
        string Instruction,
        [property: JsonPropertyName("ingredient_indexes")] List<int> IngredientIndexes
    );

    internal record MatchingResponse(List<MatchResult> Results);

    internal record MatchResult(
        [property: JsonPropertyName("scraped_index")]           int ScrapedIndex,
        [property: JsonPropertyName("candidate_index")]         int? CandidateIndex,
        [property: JsonPropertyName("confidence")]              double Confidence,
        [property: JsonPropertyName("suggested_category")]      string SuggestedCategory,
        [property: JsonPropertyName("suggested_default_unit")]  string? SuggestedDefaultUnit
    );
}

// ── Exception types ───────────────────────────────────────────────────────────

public enum RecipeScrapeError
{
    FetchTimeout,
    FetchFailed,
    FetchNonSuccess,
    LlmTimeout,
    LlmFailed,
    NoContent,
}

public class RecipeScrapeException(RecipeScrapeError error, string message)
    : Exception(message)
{
    public RecipeScrapeError Error { get; } = error;
}
