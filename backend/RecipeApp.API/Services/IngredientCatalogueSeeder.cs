using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Data;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;
using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.Services;

public class IngredientCatalogueSeeder(
    ILlmStructuredClient llm,
    AppDbContext db,
    ILogger<IngredientCatalogueSeeder> logger)
{
    private const string CatalogueSchemaJson = """
        {
          "type": "object",
          "properties": {
            "ingredients": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "name":         { "type": "string" },
                  "display_name": { "type": "string" },
                  "category":     { "type": "string" },
                  "default_unit": { "type": "string" }
                },
                "required": ["name", "display_name", "category", "default_unit"]
              }
            }
          },
          "required": ["ingredients"]
        }
        """;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy   = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public async Task SeedAsync(int targetCount = 200, CancellationToken ct = default)
    {
        var categoriesStr = string.Join(", ", IngredientCategory.All);
        var unitsStr      = string.Join(", ", MeasurementUnit.All);

        const int batchSize = 50;
        var namesProducedSoFar = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Load existing names so we can avoid re-requesting them
        var existingNames = await db.Ingredients
            .AsNoTracking()
            .Select(i => i.Name)
            .ToListAsync(ct);
        foreach (var n in existingNames) namesProducedSoFar.Add(n);

        int inserted = 0;
        int skipped  = 0;
        int requested = 0;

        var schema = JsonNode.Parse(CatalogueSchemaJson)!;

        while (requested < targetCount)
        {
            var remaining    = Math.Min(batchSize, targetCount - requested);
            var excludeClause = namesProducedSoFar.Count > 0
                ? $"\nDo NOT include any of these already-produced names: {string.Join(", ", namesProducedSoFar.Take(200))}."
                : string.Empty;

            var systemPrompt =
                $"You are a culinary data assistant. Generate a list of common household cooking ingredients.\n" +
                $"Each item must have: name (lowercase, singular, normalised), display_name (title case), " +
                $"category (one of: {categoriesStr}), default_unit (one of: {unitsStr}).\n" +
                $"Cover a variety of categories. Avoid duplicates.{excludeClause}\n" +
                "Respond with a single JSON object conforming to the schema and nothing else.";

            var userContent = $"Generate exactly {remaining} common cooking ingredients.";

            logger.LogInformation("Requesting batch of {Count} ingredients (produced so far: {Total})…",
                remaining, namesProducedSoFar.Count - existingNames.Count + inserted);

            JsonNode batchJson;
            try
            {
                batchJson = await llm.CompleteStructuredAsync(
                    systemPrompt, userContent, schema, maxTokens: 4096, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "LLM call failed for catalogue batch; aborting.");
                break;
            }

            var batchResponse = batchJson.Deserialize<CatalogueBatchResponse>(JsonOpts);
            if (batchResponse?.Ingredients == null || batchResponse.Ingredients.Count == 0)
            {
                logger.LogWarning("Empty batch returned; stopping.");
                break;
            }

            requested += batchResponse.Ingredients.Count;

            foreach (var item in batchResponse.Ingredients)
            {
                var normName = item.Name?.Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(normName)) { skipped++; continue; }
                if (!namesProducedSoFar.Add(normName)) { skipped++; continue; }

                var category = IngredientCategory.IsValid(item.Category)
                    ? item.Category
                    : RecipeScrapeService.CategoriseIngredient(normName);

                // The model is free to answer "teaspoons" or "ML"; canonicalise before storing so
                // every catalogue row holds a spelling MeasurementUnit.IsValid accepts.
                var unit = MeasurementUnit.TryCanonicalise(item.DefaultUnit, out var canonicalUnit)
                    ? canonicalUnit
                    : MeasurementUnit.Piece;

                var existing = await db.Ingredients
                    .FirstOrDefaultAsync(i => i.Name == normName, ct);

                if (existing != null) { skipped++; continue; }

                db.Ingredients.Add(new Ingredient
                {
                    Id          = Guid.NewGuid(),
                    Name        = normName,
                    DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? normName : item.DisplayName.Trim(),
                    Category    = category,
                    DefaultUnit = unit,
                    CreatedAt   = DateTime.UtcNow,
                });
                inserted++;
            }

            await db.SaveChangesAsync(ct);
        }

        logger.LogInformation(
            "Catalogue seed complete. Requested: {Requested}, Inserted: {Inserted}, Skipped: {Skipped}.",
            requested, inserted, skipped);
    }

    private record CatalogueBatchResponse(List<CatalogueItem> Ingredients);

    private record CatalogueItem(
        string Name,
        [property: JsonPropertyName("display_name")] string DisplayName,
        string Category,
        [property: JsonPropertyName("default_unit")] string DefaultUnit
    );
}
