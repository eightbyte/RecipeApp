namespace RecipeApp.Tests.Services;

/// <summary>A hand-written page in the shape Mealime serves, with made-up recipe content, so no copy of a real page is kept.</summary>
internal static class MealimePageFixture
{
    public const string Url = "https://www.mealime.com/recipes/lemon-chickpea-bowls/123";

    /// <summary>JSON-LD whose list states no amount for salt or oil, with the amounts only in each step's <c>secondary_message</c>.</summary>
    public const string Html = """
        <html><head>
        <script type="application/ld+json">
        {
          "@context": "https://schema.org",
          "@type": "Recipe",
          "name": "Lemon Chickpea Bowls",
          "recipeYield": "2",
          "recipeIngredient": ["1 can chickpeas", "olive oil", "salt"],
          "recipeInstructions": [
            { "@type": "HowToStep", "text": "Heat the oven." },
            { "@type": "HowToStep", "text": "Toss the chickpeas with oil and salt." },
            { "@type": "HowToStep", "text": "Season with more salt and serve." }
          ]
        }
        </script>
        </head><body><div id="__next"></div>
        <script id="__NEXT_DATA__" type="application/json">
        {
          "props": {
            "pageProps": {
              "publishedRecipe": {
                "id": 1,
                "name": "Lemon Chickpea Bowls",
                "instructions": [
                  { "id": 11, "primary_message": "Heat the oven.", "secondary_message": null },
                  { "id": 12, "primary_message": "Toss the chickpeas with oil and salt.", "secondary_message": "1 can chickpeas\n1 tbsp olive oil\n½ tsp salt" },
                  { "id": 13, "primary_message": "Season with more salt and serve.", "secondary_message": "½ tsp salt" }
                ]
              }
            }
          },
          "page": "/recipes/[slug]/[id]"
        }
        </script>
        </body></html>
        """;
}
