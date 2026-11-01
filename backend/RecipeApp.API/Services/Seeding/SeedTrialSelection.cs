namespace RecipeApp.API.Services.Seeding;

/// <summary>
/// The 20 recipes <c>seed-recipes --trial</c> imports, pinned in source so the trial is identical on every machine and
/// stratified so it hits the hard cases, each line commented with the stratum it was picked for.
/// </summary>
public static class SeedTrialSelection
{
    public static readonly IReadOnlyList<string> Slugs =
    [
        // ── Strata ────────────────────────────────────────────────────────────
        "apple-crisp-0",              // baking: dry cups of flour, sugar and oats (density coverage)
        "red-beans-and-rice1",        // canned goods: 1 can (14.5 ounces)
        "ratatouille-0",              // whole produce counted as pcs: 2 medium …
        "baked-fish-0",               // long instructions: 9 steps
        "grape-salsa",                // very short instructions: 2 steps
        "cinnamon-vanilla-granola",   // footnoted notes (*) — must not leak into a step
        "fried-rice-0",               // mixed-number fractions: 1 1/2 …
        "bugs-log",                   // four or more unquantified rows (7), plus three headings
        "corn-casserole-0",           // unquantified
        "black-bean-soup-0",          // unquantified, plus a can-count amount Stage 5 drops
        "cafe-mocha",                 // unquantified
        "cherry-puff-pancake",        // a vulgar fraction with no ASCII digit: ¼ cup sliced almonds
        "potato-salad-0",             // adapted-source credit and page notes, both into Description
        "picadillo-0",                // cups of rice (density coverage)

        // ── Stage 5's own decisions ───────────────────────────────────────────
        "turnip-pancakes",            // group headings skipped: Pancakes, Dipping Sauce
        "sunshine-salad",             // an unexplained multi-number amount (13.333 cups) dropped
        "splendid-fruit-salad",       // no harvested photo: ImageUrl stays null
        "red-beans-and-rice-0",       // cups of a liquid, which stay cup
        "oven-baked-potato-pancakes", // an either/or name resolved through an added alias
        "red-bean-quesadilla",        // cups of greens, which have no density and stay cup
    ];
}
