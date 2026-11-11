using System.Globalization;
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
    IModelServerStatusProbe modelServer,
    AppDbContext db,
    IHeadlessRenderer headlessRenderer,
    MeasurementConverter measurementConverter,
    IEnumerable<ISiteReader> siteReaders,
    ImageService images,
    ILogger<RecipeScrapeService> logger) : IRecipeScrapeService
{
    private readonly RecipeScrapingOptions _options = options.Value;

    // ── Recipe extraction schema ──────────────────────────────────────────────

    /// <summary>
    /// The one extraction schema shared by the scrape and the seed normaliser, where every property a caller wants back must be
    /// required because the grammar cannot emit an optional one.
    /// </summary>
    internal const string RecipeSchemaJson = """
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
                    "type": ["number", "null"],
                    "description": "Numeric quantity exactly as stated on the page. Null if the line states no quantity (e.g. 'salt', 'salt and pepper to taste', 'nonstick cooking spray'). Never invent a quantity."
                  },
                  "unit": {
                    "type": ["string", "null"],
                    "description": "Unit of measurement as stated on the page. Preserve the original unit; do not convert. Null when amount is null."
                  },
                  "notes": {
                    "type": ["string", "null"],
                    "description": "Preparation and descriptive detail (e.g. 'finely chopped', 'optional', 'low-sodium', the container or size word). Null if none."
                  }
                },
                "required": ["name", "display_name", "amount", "unit", "notes"]
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
          "required": ["name", "description", "servings", "ingredients", "steps"]
        }
        """;

    /// <summary>The scrape's own schema: <c>RecipeSchemaJson</c> plus the amounts each step states, kept apart so the seed normaliser's output is unchanged.</summary>
    internal static readonly string ScrapeSchemaJson = WithStatedAmounts(RecipeSchemaJson);

    private static string WithStatedAmounts(string recipeSchemaJson)
    {
        var schema = JsonNode.Parse(recipeSchemaJson)!;
        var step   = schema["properties"]!["steps"]!["items"]!;

        step["properties"]!["stated_amounts"] = JsonNode.Parse($$"""
            {
              "type": "array",
              "description": "Every amount this step states for an ingredient, in its own text or in the lines under its '{{RecipeJsonLdExtractor.StepLinesHeading}}' heading. Empty when it states none.",
              "items": {
                "type": "object",
                "properties": {
                  "ingredient_index": {
                    "type": "integer",
                    "description": "0-based index into the ingredients array of the ingredient the amount is for."
                  },
                  "amount": {
                    "type": ["number", "null"],
                    "description": "Numeric quantity exactly as the step states it. Null when the step names a unit with no number (e.g. 'a pinch')."
                  },
                  "unit": {
                    "type": ["string", "null"],
                    "description": "Unit as the step states it; do not convert. Null for a plain count (e.g. '2 eggs')."
                  }
                },
                "required": ["ingredient_index", "amount", "unit"]
              }
            }
            """);
        step["required"]!.AsArray().Add("stated_amounts");

        return schema.ToJsonString();
    }

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
        "Never invent a quantity: if an ingredient line states no amount, emit null for both amount and unit. " +
        "For a counted ingredient with no unit (e.g. '2 eggs'), emit the amount and a null unit, " +
        "and put a size word such as 'large' in notes rather than in unit. " +
        "For each step, list in stated_amounts every amount the step states for an ingredient, whether in the step's own text " +
        $"(e.g. 'drizzle with 2 tbsp oil') or in the lines under its '{RecipeJsonLdExtractor.StepLinesHeading}' heading. " +
        "Wording with no amount (e.g. 'a drizzle of oil', 'season with salt') states none, so leave it out. " +
        "A step's instruction is its own text only, without the lines under that heading. " +
        "Respond with a single JSON object conforming to the schema and nothing else.";

    // ── Ingredient category heuristic ─────────────────────────────────────────

    // First match wins, so order settles substring collisions: a category built from other aisles' words must lead
    // (SOUPS_BROTH before MEAT_SEAFOOD, KITCHEN before CONDIMENTS, GRAINS_RICE before BAKERY).
    private static readonly (string Category, string[] Keywords)[] CategoryPriority =
    [
        (IngredientCategory.SoupsBroth,   ["broth", "stock", "bouillon", "consomme", "consommé", "soup", "bisque", "chowder"]),
        (IngredientCategory.MeatSeafood,  ["chicken", "beef", "pork", "lamb", "turkey", "duck", "bacon", "ham", "sausage", "mince", "steak", "fillet", "breast", "thigh", "salmon", "tuna", "cod", "prawn", "shrimp", "crab", "lobster", "mussel", "anchovy", "chorizo", "salami", "pepperoni"]),
        (IngredientCategory.JamNutButter, ["peanut butter", "almond butter", "cashew butter", "nut butter", "seed butter", "jam", "jelly", "preserves", "marmalade", "honey", "syrup"]),
        (IngredientCategory.PastaSauces,  ["pasta", "spaghetti", "macaroni", "noodle", "penne", "lasagna", "linguine", "fettuccine", "rotini", "fusilli", "orzo", "marinara", "pizza sauce"]),
        (IngredientCategory.Dairy,        ["milk", "cream", "butter", "margarine", "cheese", "yogurt", "yoghurt", "egg", "parmesan", "mozzarella", "cheddar", "ricotta", "brie", "ghee", "crème fraîche", "sour cream"]),
        (IngredientCategory.Canned,       ["canned", "tinned", "kidney bean", "chickpea", "black bean", "cannellini", "coconut milk", "chopped tomato", "diced tomato", "tomato paste", "tomato sauce"]),
        (IngredientCategory.Frozen,       ["frozen"]),
        (IngredientCategory.Kitchen,      ["foil", "parchment", "wax paper", "plastic wrap", "skewer", "toothpick", "muffin liner", "cupcake liner", "paper cup", "popsicle stick", "craft stick"]),
        (IngredientCategory.CoffeeTea,    ["coffee", "espresso", "tea"]),
        (IngredientCategory.Beverages,    ["wine", "beer", "juice"]),
        (IngredientCategory.Condiments,   ["oil", "cooking spray", "vinegar", "sauce", "soy", "fish sauce", "worcestershire", "mustard", "ketchup", "mayonnaise", "miso", "tahini", "curry paste"]),
        (IngredientCategory.BakingSpices, ["flour", "sugar", "salt", "baking powder", "baking soda", "yeast", "cornstarch", "cornflour", "cornmeal", "cocoa", "chocolate", "gelatin", "vanilla", "extract", "black pepper", "peppercorn", "paprika", "cumin", "turmeric", "cinnamon", "nutmeg", "curry powder", "chili powder", "chilli flake", "pepper flake", "cayenne", "garlic powder", "onion powder", "oregano", "seasoning", "spice", "herb"]),
        (IngredientCategory.GrainsRice,   ["rice", "oat", "quinoa", "couscous", "barley", "bulgur", "farro", "millet", "grits", "polenta"]),
        (IngredientCategory.Bakery,       ["bread", "roll", "bun", "baguette", "pita", "tortilla", "wrap", "crumpet", "croissant", "pizza crust", "pizza shell"]),
        (IngredientCategory.DryGoods,     ["breadcrumb", "lentil", "almond", "walnut", "cashew", "peanut", "sesame", "seed", "nut"]),
        (IngredientCategory.Produce,      ["onion", "garlic", "carrot", "celery", "tomato", "potato", "lettuce", "spinach", "kale", "broccoli", "pepper", "capsicum", "zucchini", "cucumber", "avocado", "lemon", "lime", "orange", "apple", "banana", "mushroom", "corn", "asparagus", "pea", "parsley", "basil", "coriander", "thyme", "rosemary", "mint", "dill", "chive", "scallion", "leek", "shallot", "ginger", "chilli", "eggplant", "beetroot", "pumpkin", "squash", "berry"]),
    ];

    /// <summary>How every response to a <c>RecipeSchemaJson</c>-shaped prompt is read, shared with the seed normaliser, which sends the same schema.</summary>
    internal static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling              = JsonNumberHandling.AllowReadingFromString,
    };

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<ScrapePreviewResponse> ScrapeAsync(string url, CancellationToken ct = default)
    {
        // Checked before the fetch, which can take 35 seconds and is wasted when nothing can read the page.
        var status = await modelServer.GetStatusAsync(ct);
        if (status.State != ModelServerState.Ready)
            throw UnavailableError(status.State);

        logger.LogInformation("Scraping {Url} with model {Model}.", url, status.Model ?? "(unnamed)");

        var page      = await FetchPageAsync(url, ct);
        var extracted = await ExtractRecipeAsync(page.Text, ct);
        return await NormaliseAsync(extracted, url, ct) with { ImageUrl = page.ImageUrl };
    }

    public async Task<ScrapeConfirmResponse> ConfirmAsync(ScrapeConfirmRequest request, CancellationToken ct = default)
    {
        // Downloaded only now, so an abandoned preview leaves no file behind.
        var imageUrl = request.ImageUrl is null ? null : await StorePhotoAsync(request.ImageUrl, ct);

        Guid recipeId;
        try
        {
            recipeId = await AddRecipeAsync(request, imageUrl, ct);
        }
        catch
        {
            images.Delete(imageUrl);
            throw;
        }

        var full = await db.Recipes
            .Include(r => r.Ingredients).ThenInclude(ri => ri.Ingredient)
            .Include(r => r.Ingredients).ThenInclude(ri => ri.StepIngredients)
            .Include(r => r.Steps).ThenInclude(rs => rs.StepIngredients)
            .AsNoTracking()
            .FirstAsync(r => r.Id == recipeId, ct);

        return new ScrapeConfirmResponse(full.ToDetail(), PhotoSkipped: request.ImageUrl is not null && imageUrl is null);
    }

    /// <summary>Downloads the photo and stores it under the upload rules, where any failure means no photo rather than a failed save.</summary>
    /// <returns>The stored photo's public URL, or null when it was skipped.</returns>
    private async Task<string?> StorePhotoAsync(string url, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient("RecipeScraper");

        using var fetchCts  = new CancellationTokenSource(TimeSpan.FromSeconds(_options.ImageFetchTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, fetchCts.Token);

        try
        {
            // Leaves out avif, which storage refuses, so a negotiating image CDN serves a format it accepts.
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Accept.ParseAdd("image/webp,image/jpeg,image/png,image/*;q=0.8");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, linkedCts.Token);
            if (!response.IsSuccessStatusCode)
                return SkipPhoto(url, $"HTTP {(int)response.StatusCode}");

            var content = await ReadUpToAsync(response.Content, images.MaxBytes, linkedCts.Token);
            if (content is null)
                return SkipPhoto(url, $"larger than {images.MaxBytes / 1024 / 1024} MB");

            return await images.SaveDownloadAsync(content, ct)
                ?? SkipPhoto(url, "not a jpg, png or webp image");
        }
        catch (OperationCanceledException) when (fetchCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            return SkipPhoto(url, $"no response within {_options.ImageFetchTimeoutSeconds} s");
        }
        // An IOException is a connection dropped mid-body or a failed file write, neither of which may fail the save.
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            return SkipPhoto(url, ex.Message);
        }
    }

    private string? SkipPhoto(string url, string reason)
    {
        logger.LogWarning("Saving the recipe without its photo {Url}: {Reason}.", url, reason);
        return null;
    }

    /// <summary>The whole body, or null as soon as it passes <paramref name="maxBytes"/>, so an oversized photo is never held in memory.</summary>
    private static async Task<byte[]?> ReadUpToAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maxBytes) return null;

        await using var stream = await content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var chunk = new byte[81_920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > maxBytes) return null;
        }
        return buffer.ToArray();
    }

    private async Task<Guid> AddRecipeAsync(ScrapeConfirmRequest request, string? imageUrl, CancellationToken ct)
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
            ImageUrl    = imageUrl,
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
            var stored = MeasurementConverter.ToStored(ing.Amount, ing.Unit, ing.SourceAmount, ing.SourceUnit);
            var ri = new RecipeIngredient
            {
                Id           = Guid.NewGuid(),
                RecipeId     = recipe.Id,
                IngredientId = resolvedIngredientIds[i],
                Amount       = stored.Amount,
                Unit         = stored.Unit,
                SourceAmount = stored.SourceAmount,
                SourceUnit   = stored.SourceUnit,
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
        return recipe.Id;
    }

    // ── HTML fetching and stripping ───────────────────────────────────────────

    /// <summary>The page's recipe text, with the photo from the same document as the text, or from the other document when that one has none.</summary>
    public async Task<FetchedPage> FetchPageAsync(string url, CancellationToken ct)
    {
        var html = await FetchHtmlAsync(url, ct);
        var fetched = await ExtractCandidateAsync(html, url);
        if (fetched.Sufficient || !_options.EnableHeadlessFallback) return fetched.Page;

        try
        {
            var renderedHtml = await headlessRenderer.RenderAsync(
                url, TimeSpan.FromSeconds(_options.HeadlessRenderTimeoutSeconds), ct);
            var rendered = await ExtractCandidateAsync(renderedHtml, url);

            // Prefer the rendered result if it cleared the bar or is simply more substantial than the static fetch.
            return rendered.Sufficient || rendered.Page.Text.Length > fetched.Page.Text.Length
                ? rendered.Page with { ImageUrl = rendered.Page.ImageUrl ?? fetched.Page.ImageUrl }
                : fetched.Page with { ImageUrl = fetched.Page.ImageUrl ?? rendered.Page.ImageUrl };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Headless render fallback failed for {Url}; using static fetch result.", url);
        }

        return fetched.Page;
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

    /// <summary>Tries JSON-LD recipe markup first, falling back to stripped visible body text, and reports whether the result is sufficient for the caller to stop trying.</summary>
    private async Task<(FetchedPage Page, bool Sufficient)> ExtractCandidateAsync(string html, string url)
    {
        var parser = new HtmlParser();
        var doc    = await parser.ParseDocumentAsync(html);

        // Read before stripping, because stripping removes the scripts and meta tags they come from.
        var stepLines = ReadStepLines(doc, url);
        var imageUrl  = Uri.TryCreate(url, UriKind.Absolute, out var pageUrl) ? RecipePhotoLocator.TryFind(doc, pageUrl) : null;

        var jsonLdText = RecipeJsonLdExtractor.TryBuildRecipeText(doc, stepLines);
        if (jsonLdText is not null) return (new FetchedPage(jsonLdText, imageUrl), true);

        // Judged on the page's own text, because a reader's lines alone do not make a thin static fetch readable.
        var strippedText = StripBodyText(doc);
        return (new FetchedPage(strippedText + RecipeJsonLdExtractor.BuildStepLinesSection(stepLines), imageUrl),
            strippedText.Length >= _options.MinStaticContentLength);
    }

    /// <summary>The extra lines per step from the Site Reader for the URL's host, or null when no reader handles it or the reader fails, which never fails the scrape.</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<string>>? ReadStepLines(IDocument doc, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;

        var reader = siteReaders.FirstOrDefault(r => r.Hosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase));
        if (reader is null) return null;

        try
        {
            var lines = reader.ReadStepLines(doc);
            if (lines is null)
                logger.LogWarning("{Reader} found no page data in the shape it expects on {Url}; scraping without it.",
                    reader.GetType().Name, url);
            return lines;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "{Reader} failed on {Url}; scraping without it.", reader.GetType().Name, url);
            return null;
        }
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
        var schema      = JsonNode.Parse(ScrapeSchemaJson)!;

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
        catch (ModelServerUnavailableException ex)
        {
            throw UnavailableError(ex.State);
        }
        catch (Exception ex) when (ex is not RecipeScrapeException)
        {
            throw new RecipeScrapeException(RecipeScrapeError.LlmFailed,
                "Recipe extraction failed. Please try again or create the recipe manually.");
        }

        var extracted = json.Deserialize<ExtractedRecipe>(LlmJsonOptions)
            ?? throw new RecipeScrapeException(RecipeScrapeError.NoContent,
                "No recipe content could be extracted from this page.");

        return extracted;
    }

    private static RecipeScrapeException UnavailableError(ModelServerState state) =>
        new(RecipeScrapeError.ModelServerUnavailable, $"Scraping is unavailable: {state.UnavailableReason()}");

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
            .Select(i => new { i.Id, i.Name, i.DisplayName, i.Category, i.GramsPerMillilitre, i.Aliases })
            .ToListAsync(ct);

        // Keyed by name and by every alias so a known synonym resolves here, with names loaded first so a name always outranks an alias.
        var exactLookup = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var ingredient in dbIngredients)
            exactLookup[ingredient.Name] = ingredient.Id;

        foreach (var ingredient in dbIngredients)
            foreach (var alias in ingredient.Aliases)
                exactLookup.TryAdd(alias, ingredient.Id);

        var densityById = dbIngredients
            .Where(i => i.GramsPerMillilitre.HasValue)
            .ToDictionary(i => i.Id, i => i.GramsPerMillilitre);

        decimal? DensityOf(Guid? ingredientId) =>
            ingredientId is { } id && densityById.TryGetValue(id, out var density) ? density : null;

        // Stage B: resolve an import-resolvable unit against the matched ingredient's density, leaving an unmatched or unquantified row as stated.
        ScrapePreviewIngredientResponse ResolveAgainstCatalogue(ScrapePreviewIngredientResponse row)
        {
            if (row.IngredientId is null) return row;
            if (row.Amount is not { } statedAmount || row.Unit is not { } statedUnit) return row;

            var (resolvedAmount, resolvedUnit) =
                measurementConverter.ResolveForImport(statedAmount, statedUnit, DensityOf(row.IngredientId));
            return row with { Amount = resolvedAmount, Unit = resolvedUnit };
        }

        // Pass 1: exact match
        var previewIngredients = new List<ScrapePreviewIngredientResponse>();
        var unmatchedIndexes   = new List<int>();

        for (int i = 0; i < extracted.Ingredients.Count; i++)
        {
            var ing            = extracted.Ingredients[i];
            var normalisedName = ing.Name.Trim().ToLowerInvariant();

            var (amount, unit, sourceAmount, sourceUnit, notes) = ReadStatedMeasurement(ing);

            var row = new ScrapePreviewIngredientResponse(
                IngredientId:      null,
                Name:              normalisedName,
                DisplayName:       ing.DisplayName,
                Amount:            amount,
                Unit:              unit,
                SourceAmount:      sourceAmount,
                SourceUnit:        sourceUnit,
                EntryUnit:         null,
                Notes:             notes,
                IsNew:             true,
                SuggestedCategory: CategoriseIngredient(normalisedName),
                DisplayOrder:      i,
                AmountFromSteps:   false,
                StepAmounts:       []);

            if (exactLookup.TryGetValue(normalisedName, out var existingId))
            {
                row = row with
                {
                    IngredientId      = existingId,
                    IsNew             = false,
                    SuggestedCategory = IngredientCategory.Other,
                };
            }
            else
            {
                unmatchedIndexes.Add(i);
            }

            previewIngredients.Add(row);
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

            // Its own budget, because the Model Server queues behind other callers and the client has no timeout.
            using var matchCts    = new CancellationTokenSource(TimeSpan.FromSeconds(_options.LlmTimeoutSeconds));
            using var matchLinked = CancellationTokenSource.CreateLinkedTokenSource(ct, matchCts.Token);

            try
            {
                var matchJson = await llm.CompleteStructuredAsync(
                    systemPrompt, userContent, matchSchema, maxTokens: 2048, matchLinked.Token);

                var matchResponse = matchJson.Deserialize<MatchingResponse>(LlmJsonOptions);
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

        // Filled once matching is done, because adding a volume to a mass needs the matched Ingredient's density, and before Stage B so a filled cup resolves too.
        var stepAmounts = StepAmountsByIngredient(extracted, url);
        for (int i = 0; i < previewIngredients.Count; i++)
        {
            var row = previewIngredients[i];
            if (stepAmounts.TryGetValue(i, out var mentions))
                row = FillFromSteps(row, extracted.Ingredients[i], mentions, DensityOf(row.IngredientId));
            previewIngredients[i] = ResolveAgainstCatalogue(row);
        }

        var previewSteps = extracted.Steps
            .OrderBy(s => s.StepNumber)
            .Select(s => new ScrapePreviewStepResponse(
                s.StepNumber, WithoutStepLines(s.Instruction), DistinctIngredientIndexesInRange(s, previewIngredients.Count, url)))
            .ToList();

        return new ScrapePreviewResponse(
            Name:        extracted.Name.Trim(),
            Description: string.IsNullOrWhiteSpace(extracted.Description) ? null : extracted.Description.Trim(),
            Servings:    extracted.Servings > 0 ? extracted.Servings : 4,
            SourceUrl:   url,
            // Named once every row is final, because matching can resolve a row's measurement against a density.
            Ingredients: previewIngredients
                .Select(row => row with
                {
                    EntryUnit = MeasurementConverter.EntryUnitOf(row.Amount, row.Unit, row.SourceAmount, row.SourceUnit),
                })
                .ToList(),
            Steps:       previewSteps
        );
    }

    /// <summary>The model can number past the end of the ingredients, and the confirm validator rejects such a link, so a preview carrying one could never be saved.</summary>
    private List<int> DistinctIngredientIndexesInRange(ExtractedStep step, int ingredientCount, string url)
    {
        var byRange = step.IngredientIndexes.Distinct().ToLookup(i => i >= 0 && i < ingredientCount);
        var kept    = byRange[true].ToList();
        var dropped = byRange[false].ToList();

        if (dropped.Count > 0)
            logger.LogWarning(
                "Scrape of {Url} linked step {StepNumber} to ingredient indexes {DroppedIndexes} outside 0–{LastIndex}; dropped them.",
                url, step.StepNumber, string.Join(", ", dropped), ingredientCount - 1);

        return kept;
    }

    /// <summary>A step's instruction without any Site Reader lines the model copied into it, because saved text does not scale with Servings.</summary>
    private static string WithoutStepLines(string instruction)
    {
        var heading = instruction.IndexOf(RecipeJsonLdExtractor.StepLinesHeading, StringComparison.OrdinalIgnoreCase);
        return heading < 0 ? instruction : instruction[..heading].TrimEnd();
    }

    // ── Amounts from steps ────────────────────────────────────────────────────

    /// <summary>The amounts every step states, in step order, keyed by the index of the ingredient each is for, keeping only those that state something.</summary>
    private Dictionary<int, List<StepAmount>> StepAmountsByIngredient(ExtractedRecipe extracted, string url)
    {
        var byIngredient = new Dictionary<int, List<StepAmount>>();
        foreach (var step in extracted.Steps.OrderBy(s => s.StepNumber))
        {
            var dropped = new List<int>();
            foreach (var stated in step.StatedAmounts ?? [])
            {
                if (stated.IngredientIndex < 0 || stated.IngredientIndex >= extracted.Ingredients.Count)
                {
                    dropped.Add(stated.IngredientIndex);
                    continue;
                }

                var amount = stated.Amount is > 0 ? Math.Round((decimal)stated.Amount.Value, 3) : (decimal?)null;
                var unit   = string.IsNullOrWhiteSpace(stated.Unit) ? null : stated.Unit.Trim();
                if (amount is null && !SizelessUnit.IsSizeless(unit)) continue;

                if (!byIngredient.TryGetValue(stated.IngredientIndex, out var mentions))
                    byIngredient[stated.IngredientIndex] = mentions = [];
                mentions.Add(new StepAmount(amount, unit));
            }

            if (dropped.Count > 0)
                logger.LogWarning(
                    "Scrape of {Url} gave step {StepNumber} amounts for ingredient indexes {DroppedIndexes} outside 0–{LastIndex}; dropped them.",
                    url, step.StepNumber, string.Join(", ", dropped), extracted.Ingredients.Count - 1);
        }
        return byIngredient;
    }

    /// <summary>
    /// Fills a row whose list line states nothing, not even a Sizeless Unit, from the total of its step amounts, leaving it
    /// empty but listing the amounts when they cannot be added up; a row whose line states anything keeps it.
    /// </summary>
    private static ScrapePreviewIngredientResponse FillFromSteps(
        ScrapePreviewIngredientResponse row, ExtractedIngredient ing, List<StepAmount> mentions, decimal? density)
    {
        if (row.Amount is not null || row.SourceAmount is not null || row.SourceUnit is not null) return row;

        var withStepAmounts = row with
        {
            StepAmounts = mentions.Select(m => new ScrapePreviewStepAmountResponse(m.Amount, m.Unit)).ToList(),
        };

        switch (StepAmountTotaller.Total(mentions, row.Name, density))
        {
            case StepAmountTotal.InSharedUnit shared:
                // Read like a stated line, so a Count Word, an Entry-only Unit or a cup is stored as one would be.
                var (amount, unit, sourceAmount, sourceUnit, notes) =
                    ReadStatedMeasurement(ing with { Amount = (double)shared.Amount, Unit = shared.Unit });
                return withStepAmounts with
                {
                    Amount = amount, Unit = unit, SourceAmount = sourceAmount, SourceUnit = sourceUnit, Notes = notes,
                    AmountFromSteps = true,
                };

            case StepAmountTotal.Converted converted:
                return withStepAmounts with { Amount = converted.Amount, Unit = converted.Unit, AmountFromSteps = true };

            case StepAmountTotal.Sizeless sizeless:
                return withStepAmounts with { SourceAmount = sizeless.SourceAmount, SourceUnit = sizeless.Word, AmountFromSteps = true };

            case StepAmountTotal.NotAddedUp:
                return withStepAmounts;

            default:
                return row;
        }
    }

    /// <summary>
    /// Stage A for one scraped line, which converts its measurement and records the source verbatim so a conversion can be
    /// audited, storing a Sizeless Unit unquantified, a Count Word or missing unit as <c>pcs</c>, and a size word in the notes.
    /// </summary>
    private static (decimal? Amount, string? Unit, decimal? SourceAmount, string? SourceUnit, string? Notes)
        ReadStatedMeasurement(ExtractedIngredient ing)
    {
        var notes      = string.IsNullOrWhiteSpace(ing.Notes) ? null : ing.Notes;
        var statedUnit = ing.Unit;

        if (CountWords.IsSizeWord(statedUnit))
        {
            notes      = WithLeadingNote(notes, statedUnit.Trim());
            statedUnit = null;
        }

        if (ing.Amount is not { } statedAmount)
            return SizelessUnit.IsSizeless(statedUnit)
                ? (null, null, null, statedUnit.Trim(), notes)
                : (null, null, null, null, notes);

        // Nothing is lost by storing a bare amount as pcs, so it records no Source Measurement.
        if (string.IsNullOrWhiteSpace(statedUnit))
            return (Math.Round((decimal)statedAmount, 3), MeasurementUnit.Piece, null, null, notes);

        var sourceAmount = Math.Round((decimal)statedAmount, 3);
        var sourceUnit   = statedUnit.Trim();

        if (SizelessUnit.IsSizeless(statedUnit))
            return (null, null, sourceAmount, sourceUnit, notes);

        if (CountWords.IsCountWord(statedUnit, ing.Name))
            return (sourceAmount, MeasurementUnit.Piece, sourceAmount, sourceUnit, notes);

        // "½ (16 oz) pkg" is stored as the 8 oz it holds, with "½ pkg" kept in the notes.
        if (ReadSizedPackage(statedAmount, sourceUnit, notes) is { } sized)
        {
            var (package, otherNotes) = sized;
            var (total, totalUnit) = MeasurementConverter.ToCanonical((double)package.Total, package.SizeUnit);
            var packageNotes = package.Container is null
                ? otherNotes
                : WithLeadingNote(otherNotes, $"{FormatCount(sourceAmount)} {package.Container}");
            return (total, totalUnit, package.Total, package.SizeUnit, packageNotes);
        }

        var (amount, unit) = MeasurementConverter.ToCanonical(statedAmount, statedUnit);
        return (amount, unit, sourceAmount, sourceUnit, notes);
    }

    /// <summary>A size leading a row's notes, bracketed or not, where the model sometimes puts the "6 oz" of "1 (6 oz) pkg".</summary>
    private static readonly Regex LeadingSizeNote = new(
        @"^\s*(?:\((?<size>[^()]*)\)|(?<size>\d+(?:\.\d+)?\s*-?\s*[a-zA-Z][a-zA-Z .]*?))\s*(?:,\s*(?<rest>.*))?$",
        RegexOptions.Compiled);

    /// <summary>Reads a sized package from the unit alone or from a package word whose size leads the notes, returning the notes left once that size is taken out.</summary>
    private static (SizedPackage Package, string? Notes)? ReadSizedPackage(double count, string unit, string? notes)
    {
        if (MeasurementConverter.ReadSizedPackage(count, unit) is { } package) return (package, notes);

        // A real unit such as "cup" is never a package, whatever its notes say.
        if (MeasurementUnit.DimensionOf(MeasurementConverter.ToCanonical(1, unit).Unit) is not null) return null;

        var leading = notes is null ? null : LeadingSizeNote.Match(notes);
        if (leading is not { Success: true }) return null;

        var combined = $"({leading.Groups["size"].Value}) {unit}";
        if (MeasurementConverter.ReadSizedPackage(count, combined) is not { } split) return null;

        var rest = leading.Groups["rest"].Value.Trim();
        return (split, rest.Length == 0 ? null : rest);
    }

    /// <summary>Writes a count of packages as a cook would, with a common fraction as its glyph.</summary>
    private static string FormatCount(decimal count)
    {
        var whole    = Math.Truncate(count);
        var fraction = (count - whole) switch
        {
            0.25m => "¼",
            0.5m  => "½",
            0.75m => "¾",
            0m    => "",
            _     => null,
        };
        if (fraction is null) return count.ToString("0.###", CultureInfo.InvariantCulture);
        return whole == 0 && fraction.Length > 0 ? fraction : $"{whole:0}{fraction}";
    }

    /// <summary>Puts a word at the front of a row's notes, unless the notes already say it.</summary>
    private static string WithLeadingNote(string? notes, string word)
    {
        if (notes is null) return word;

        var alreadySaid = Regex.IsMatch(
            notes, $@"(?<![\w-]){Regex.Escape(word)}(?![\w-])", RegexOptions.IgnoreCase);
        return alreadySaid ? notes : $"{word}, {notes}";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public async Task<Guid> ResolveOrCreateIngredientAsync(
        string normalised, string displayName, string category, CancellationToken ct)
    {
        var existing = await db.Ingredients
            .FirstOrDefaultAsync(i => i.Name == normalised, ct);
        if (existing != null) return existing.Id;

        // An alias hit resolves to the entry that owns it, because inserting a row named like an existing alias would permanently shadow that alias.
        var byAlias = await db.Ingredients
            .FirstOrDefaultAsync(i => i.Aliases.Contains(normalised), ct);
        if (byAlias != null) return byAlias.Id;

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

    // ── Internal DTOs for LLM responses ──────────────────────────────────────

    internal record ExtractedRecipe(
        string Name,
        string? Description,
        int Servings,
        List<ExtractedIngredient> Ingredients,
        List<ExtractedStep> Steps
    );

    /// <param name="Amount">Null when the source line states no quantity, which the schema permits so the model is not forced to invent a figure.</param>
    /// <param name="Unit">Null exactly when <paramref name="Amount"/> is null.</param>
    internal record ExtractedIngredient(
        string Name,
        [property: JsonPropertyName("display_name")] string DisplayName,
        double? Amount,
        string? Unit,
        string? Notes
    );

    /// <param name="StatedAmounts">The amounts the step states, which only the scrape's schema asks for, so null from the seed normaliser.</param>
    internal record ExtractedStep(
        [property: JsonPropertyName("step_number")]       int StepNumber,
        string Instruction,
        [property: JsonPropertyName("ingredient_indexes")] List<int> IngredientIndexes,
        [property: JsonPropertyName("stated_amounts")]     List<ExtractedStepAmount>? StatedAmounts = null
    );

    internal record ExtractedStepAmount(
        [property: JsonPropertyName("ingredient_index")] int IngredientIndex,
        double? Amount,
        string? Unit
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

/// <param name="Text">The recipe text the model reads.</param>
/// <param name="ImageUrl">The page's photo as an absolute http(s) URL, or null when it offers none.</param>
public record FetchedPage(string Text, string? ImageUrl);

// ── Exception types ───────────────────────────────────────────────────────────

public enum RecipeScrapeError
{
    FetchTimeout,
    FetchFailed,
    FetchNonSuccess,
    LlmTimeout,
    LlmFailed,
    NoContent,

    /// <summary>The Model Server is offline, loading or rejecting the key, before or during extraction.</summary>
    ModelServerUnavailable,
}

public class RecipeScrapeException(RecipeScrapeError error, string message)
    : Exception(message)
{
    public RecipeScrapeError Error { get; } = error;
}
