using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AngleSharp.Html.Parser;
using Anthropic.SDK;
using Anthropic.SDK.Common;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RecipeApp.API.Data;
using RecipeApp.API.DTOs;
using RecipeApp.API.DTOs.Recipes;
using RecipeApp.API.DTOs.Scrape;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.API.Services;

public class RecipeScrapeService(
    IHttpClientFactory httpClientFactory,
    IOptions<RecipeScrapingOptions> options,
    AppDbContext db) : IRecipeScrapeService
{
    private readonly RecipeScrapingOptions _options = options.Value;

    // ── Tool schema ───────────────────────────────────────────────────────────

    private const string ToolSchemaJson = """
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
                    "description": "0-based indexes into the ingredients array for ingredients used in this step. Empty array if no specific ingredients apply."
                  }
                },
                "required": ["step_number", "instruction", "ingredient_indexes"]
              }
            }
          },
          "required": ["name", "servings", "ingredients", "steps"]
        }
        """;

    private const string SystemPrompt =
        "You are a recipe data extraction assistant. " +
        "Extract the complete recipe from the web page text provided by the user. " +
        "If any information is missing or ambiguous, make a reasonable best-guess rather than omitting it. " +
        "Return all data using the extract_recipe tool — do not include any explanation outside the tool call.";

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
        var extracted    = await CallClaudeAsync(strippedText, ct);
        return await NormaliseAsync(extracted, url, ct);
    }

    public async Task<RecipeDetailResponse> ConfirmAsync(ScrapeConfirmRequest request, CancellationToken ct = default)
    {
        // Resolve ingredient IDs — create new ones as needed
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

        // Create the recipe
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

        // Create recipe ingredients
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

        // Create steps and step-ingredient links
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
            throw new RecipeScrapeException(RecipeScrapeError.FetchNonSuccess,
                $"The recipe page returned HTTP {(int)response.StatusCode}.");

        var html = await response.Content.ReadAsStringAsync(ct);
        return await StripHtmlAsync(html);
    }

    public static async Task<string> StripHtmlAsync(string html)
    {
        var parser = new HtmlParser();
        var doc    = await parser.ParseDocumentAsync(html);

        // Remove noise elements
        string[] removeSelectors =
        [
            "script", "style", "noscript",
            "nav", "header", "footer", "aside",
            "iframe", "form", "button", "input", "select", "textarea"
        ];

        foreach (var selector in removeSelectors)
            foreach (var el in doc.QuerySelectorAll(selector).ToArray())
                el.Remove();

        // Remove display:none and visibility:hidden elements
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

    // ── Claude API call ───────────────────────────────────────────────────────

    private async Task<ClaudeExtractedRecipe> CallClaudeAsync(string strippedText, CancellationToken ct)
    {
        var client = new AnthropicClient(_options.AnthropicApiKey);

        using var claudeCts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ClaudeTimeoutSeconds));
        using var linked    = CancellationTokenSource.CreateLinkedTokenSource(ct, claudeCts.Token);

        var tool = new Function(
            "extract_recipe",
            "Extract a recipe from the given web page text and return it as structured data.",
            JsonNode.Parse(ToolSchemaJson));

        if (_options.MaxHtmlCharacters > 0 && strippedText.Length > _options.MaxHtmlCharacters)
            strippedText = strippedText[.._options.MaxHtmlCharacters];

        var userMessage = $"Extract the recipe from the following web page text:\n\n---\n{strippedText}\n---";

        var parameters = new MessageParameters
        {
            Model    = _options.Model,
            MaxTokens = 4096,
            System   = [new SystemMessage(SystemPrompt)],
            Messages =
            [
                new Message(RoleType.User, userMessage)
            ],
            Tools      = [tool],
            ToolChoice = new ToolChoice { Type = ToolChoiceType.Tool, Name = "extract_recipe" },
            Stream     = false,
        };

        MessageResponse response;
        try
        {
            response = await client.Messages.GetClaudeMessageAsync(parameters, linked.Token);
        }
        catch (OperationCanceledException) when (claudeCts.IsCancellationRequested)
        {
            throw new RecipeScrapeException(RecipeScrapeError.ClaudeTimeout,
                "Recipe extraction timed out. Please try again.");
        }
        catch (Exception ex) when (ex is not RecipeScrapeException)
        {
            throw new RecipeScrapeException(RecipeScrapeError.ClaudeFailed,
                "Recipe extraction failed. Please try again or create the recipe manually.");
        }

        var toolUse = response.Content.OfType<ToolUseContent>().FirstOrDefault()
            ?? throw new RecipeScrapeException(RecipeScrapeError.NoContent,
                "No recipe content could be extracted from this page.");

        var json = toolUse.Input.ToJsonString();
        var extracted = JsonSerializer.Deserialize<ClaudeExtractedRecipe>(json, JsonOptions)
            ?? throw new RecipeScrapeException(RecipeScrapeError.NoContent,
                "No recipe content could be extracted from this page.");

        return extracted;
    }

    // ── Normalisation ─────────────────────────────────────────────────────────

    private async Task<ScrapePreviewResponse> NormaliseAsync(
        ClaudeExtractedRecipe extracted, string url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(extracted.Name) ||
            extracted.Ingredients.Count == 0 ||
            extracted.Steps.Count == 0)
            throw new RecipeScrapeException(RecipeScrapeError.NoContent,
                "No recipe content could be extracted from this page.");

        // Load all ingredient names for matching (case-insensitive)
        var dbIngredients = await db.Ingredients
            .AsNoTracking()
            .Select(i => new { i.Id, i.Name })
            .ToListAsync(ct);

        var ingredientLookup = dbIngredients
            .ToDictionary(i => i.Name, i => i.Id, StringComparer.OrdinalIgnoreCase);

        var previewIngredients = new List<ScrapePreviewIngredient>();
        for (int i = 0; i < extracted.Ingredients.Count; i++)
        {
            var ing = extracted.Ingredients[i];
            var normalisedName = ing.Name.Trim().ToLowerInvariant();

            var (convertedAmount, convertedUnit) = ConvertUnit(ing.Amount, ing.Unit);

            if (ingredientLookup.TryGetValue(normalisedName, out var existingId))
            {
                previewIngredients.Add(new ScrapePreviewIngredient(
                    IngredientId:      existingId,
                    Name:              normalisedName,
                    DisplayName:       ing.DisplayName,
                    Amount:            convertedAmount,
                    Unit:              convertedUnit,
                    Notes:             string.IsNullOrWhiteSpace(ing.Notes) ? null : ing.Notes,
                    IsNew:             false,
                    SuggestedCategory: IngredientCategory.Other,
                    DisplayOrder:      i
                ));
            }
            else
            {
                previewIngredients.Add(new ScrapePreviewIngredient(
                    IngredientId:      null,
                    Name:              normalisedName,
                    DisplayName:       ing.DisplayName,
                    Amount:            convertedAmount,
                    Unit:              convertedUnit,
                    Notes:             string.IsNullOrWhiteSpace(ing.Notes) ? null : ing.Notes,
                    IsNew:             true,
                    SuggestedCategory: CategoriseIngredient(normalisedName),
                    DisplayOrder:      i
                ));
            }
        }

        var previewSteps = extracted.Steps
            .OrderBy(s => s.StepNumber)
            .Select(s => new ScrapePreviewStep(
                s.StepNumber,
                s.Instruction,
                s.IngredientIndexes))
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

    private async Task<Guid> ResolveOrCreateIngredientAsync(
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
            // Race condition: another concurrent request created the same ingredient
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

    // ── Internal DTOs for Claude response ────────────────────────────────────

    internal record ClaudeExtractedRecipe(
        string Name,
        string? Description,
        int Servings,
        List<ClaudeIngredient> Ingredients,
        List<ClaudeStep> Steps
    );

    internal record ClaudeIngredient(
        string Name,
        [property: JsonPropertyName("display_name")] string DisplayName,
        double Amount,
        string Unit,
        string? Notes
    );

    internal record ClaudeStep(
        [property: JsonPropertyName("step_number")] int StepNumber,
        string Instruction,
        [property: JsonPropertyName("ingredient_indexes")] List<int> IngredientIndexes
    );
}

// ── Exception types ───────────────────────────────────────────────────────────

public enum RecipeScrapeError
{
    FetchTimeout,
    FetchFailed,
    FetchNonSuccess,
    ClaudeTimeout,
    ClaudeFailed,
    NoContent,
}

public class RecipeScrapeException(RecipeScrapeError error, string message)
    : Exception(message)
{
    public RecipeScrapeError Error { get; } = error;
}
