using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Enums;

namespace RecipeApp.API.Data;

/// <summary>
/// Assigns curated bulk densities to catalogue ingredients so cup measurements convert to mass by
/// arithmetic rather than by guesswork (Phase 8.5.1 §5.6).
///
/// <para><b>Curated reference data, reviewed by a human — deliberately not LLM-generated.</b>
/// Asking a model to invent densities would relocate the guessing to a place where it looks
/// authoritative, which is the exact failure mode this phase exists to remove. The set that
/// actually matters is small.</para>
///
/// <para>Only ingredients that are <i>bought by weight</i> get a density. Liquids — water, milk,
/// cream, stock, oil, juice, vinegar — and syrups and spreads sold by volume are deliberately
/// absent: converting them to grams would be correct physics and useless cooking. Anything
/// packing-dominated (leafy greens, cubed bread, mixed salad) is absent for the opposite reason —
/// no single figure is true. A null density is the correct outcome for both, not a gap to fill.</para>
///
/// <para>The seeder is idempotent and only ever fills a null, so it is safe to re-run after the
/// catalogue grows and it never overwrites a density corrected by hand.</para>
/// </summary>
public static class IngredientDensitySeeder
{
    /// <summary>
    /// One curated density.
    /// </summary>
    /// <param name="ReferenceName">The name the published figure is quoted against.</param>
    /// <param name="CatalogueNames">
    /// Normalised catalogue names this density applies to. Keyed as a list because catalogue
    /// entries rarely match the reference name — the starter catalogue says <c>plain flour</c>,
    /// not <c>all-purpose flour</c>.
    /// </param>
    /// <param name="GramsPerCup">The published figure, before conversion.</param>
    /// <param name="Source">Citation, so a disputed value can be traced or an assumption checked.</param>
    public record DensityRow(
        string ReferenceName,
        string[] CatalogueNames,
        decimal GramsPerCup,
        string Source)
    {
        /// <summary>
        /// Derived by dividing the published grams-per-cup by the app's own
        /// <see cref="MeasurementUnit.MillilitresPerCup"/>, so the round trip is exact regardless
        /// of which cup definition the published table used.
        /// </summary>
        public decimal GramsPerMillilitre =>
            Math.Round(GramsPerCup / MeasurementUnit.MillilitresPerCup, 4);
    }

    private const string KingArthur  = "King Arthur Baking ingredient weight chart";
    private const string UsdaFdc     = "USDA FoodData Central";
    private const string CooksIllus  = "Cook's Illustrated conversion chart";

    public static readonly IReadOnlyList<DensityRow> Rows =
    [
        // ── Flours and starches ───────────────────────────────────────────────
        new("all-purpose flour",   ["all-purpose flour", "plain flour", "flour", "white flour"],            120, KingArthur),
        new("bread flour",         ["bread flour", "strong flour", "strong white flour"],                   120, KingArthur),
        new("whole wheat flour",   ["whole wheat flour", "wholemeal flour", "wholewheat flour"],            113, KingArthur),
        new("self-raising flour",  ["self-raising flour", "self raising flour", "self-rising flour"],        120, KingArthur),
        new("rye flour",           ["rye flour"],                                                           102, KingArthur),
        new("almond flour",        ["almond flour", "almond meal", "ground almonds"],                        96, KingArthur),
        new("cornstarch",          ["cornstarch", "corn starch", "cornflour"],                              120, KingArthur),
        new("cornmeal",            ["cornmeal", "polenta"],                                                 138, KingArthur),
        new("semolina",            ["semolina", "semolina flour"],                                          167, KingArthur),

        // ── Sugars ────────────────────────────────────────────────────────────
        new("granulated sugar",    ["granulated sugar", "sugar", "white sugar", "caster sugar", "castor sugar"], 200, KingArthur),
        new("brown sugar (packed)",
            ["brown sugar (packed)", "packed brown sugar", "brown sugar", "light brown sugar", "dark brown sugar"],
            213,
            KingArthur + " — assumes firmly packed, the standard baking convention. " +
            "A recipe that means loose brown sugar should use the separate 'brown sugar (loose)' entry."),
        new("brown sugar (loose)", ["brown sugar (loose)", "loose brown sugar"],                            145, KingArthur),
        new("powdered sugar",      ["powdered sugar", "icing sugar", "confectioners sugar", "confectioners' sugar"], 120, KingArthur),
        new("demerara sugar",      ["demerara sugar", "raw sugar", "turbinado sugar"],                      200, KingArthur),

        // ── Rice, grains and crumbs ───────────────────────────────────────────
        new("white rice (uncooked)",
            ["white rice", "rice", "long grain rice", "long-grain rice", "basmati rice", "jasmine rice"],   185, UsdaFdc),
        new("brown rice (uncooked)", ["brown rice"],                                                        190, UsdaFdc),
        new("arborio rice",        ["arborio rice", "risotto rice"],                                        200, UsdaFdc),
        new("rolled oats",         ["rolled oats", "oats", "porridge oats", "old-fashioned oats"],           90, KingArthur),
        new("quinoa (uncooked)",   ["quinoa"],                                                              170, UsdaFdc),
        new("couscous (uncooked)", ["couscous"],                                                            173, UsdaFdc),
        new("pearl barley",        ["pearl barley", "barley"],                                              200, UsdaFdc),
        new("bulgur wheat",        ["bulgur wheat", "bulgur", "bulghur"],                                   140, UsdaFdc),
        new("dry breadcrumbs",     ["dry breadcrumbs", "dried breadcrumbs", "breadcrumbs"],                 108, CooksIllus),
        new("panko breadcrumbs",   ["panko breadcrumbs", "panko"],                                           60, CooksIllus),

        // ── Dried legumes ─────────────────────────────────────────────────────
        // Only the dried forms. Canned equivalents are sold by drained weight and the catalogue
        // entry for "chickpeas" is a 400 g can, so it is deliberately not matched here.
        new("dried lentils",       ["dried lentils", "lentils", "red lentils", "green lentils"],            192, UsdaFdc),
        new("dried black beans",   ["dried black beans"],                                                   194, UsdaFdc),
        new("dried chickpeas",     ["dried chickpeas", "dried garbanzo beans"],                             200, UsdaFdc),
        new("dried kidney beans",  ["dried kidney beans"],                                                  184, UsdaFdc),
        new("split peas",          ["split peas", "dried split peas"],                                      200, UsdaFdc),

        // ── Nuts and seeds ────────────────────────────────────────────────────
        new("almonds (whole)",     ["almonds", "whole almonds"],                                            143, UsdaFdc),
        new("walnuts (halves)",    ["walnuts", "walnut halves"],                                            100, UsdaFdc),
        new("cashews",             ["cashews", "cashew nuts"],                                              137, UsdaFdc),
        new("peanuts",             ["peanuts"],                                                             146, UsdaFdc),
        new("pecans (halves)",     ["pecans", "pecan halves"],                                               99, UsdaFdc),
        new("sesame seeds",        ["sesame seeds"],                                                        144, UsdaFdc),
        new("sunflower seeds",     ["sunflower seeds"],                                                     128, UsdaFdc),
        new("chia seeds",          ["chia seeds"],                                                          170, UsdaFdc),
        new("desiccated coconut",  ["desiccated coconut", "shredded coconut"],                               80, CooksIllus),

        // ── Dairy solids and fats ─────────────────────────────────────────────
        // Sold by weight. Milk, cream and yoghurt are absent — they are bought by volume.
        new("butter",              ["butter", "unsalted butter", "salted butter"],                          227, KingArthur),
        new("grated parmesan",     ["grated parmesan", "parmesan cheese", "parmesan"],                      100, CooksIllus),
        new("shredded cheddar",    ["shredded cheddar", "grated cheddar", "cheddar cheese", "cheddar"],     113, CooksIllus),
        new("shredded mozzarella", ["shredded mozzarella", "mozzarella cheese", "mozzarella"],              112, CooksIllus),
        new("cream cheese",        ["cream cheese"],                                                        232, KingArthur),
        new("milk powder",         ["milk powder", "powdered milk", "dried milk"],                           68, KingArthur),

        // ── Baking and pantry ─────────────────────────────────────────────────
        new("cocoa powder",        ["cocoa powder", "cocoa", "unsweetened cocoa powder"],                     85, KingArthur),
        new("chocolate chips",     ["chocolate chips", "choc chips", "dark chocolate chips"],               170, KingArthur),
        new("table salt",          ["table salt", "salt", "fine salt"],                                     292, UsdaFdc),
        new("kosher salt",         ["kosher salt", "coarse salt", "sea salt flakes"],                       145, CooksIllus),
        new("raisins",             ["raisins", "sultanas"],                                                 145, UsdaFdc),
        new("dried cranberries",   ["dried cranberries"],                                                   120, UsdaFdc),
    ];

    /// <summary>
    /// Catalogue name → density. Built once; a duplicate catalogue name across two rows is a data
    /// bug and fails here rather than silently letting one row win.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, decimal> ByCatalogueName =
        Rows.SelectMany(row => row.CatalogueNames.Select(name => (name, row.GramsPerMillilitre)))
            .ToDictionary(x => x.name, x => x.GramsPerMillilitre, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Fills in the density of every catalogue ingredient that has a curated value and does not
    /// already have one. Returns the number of rows updated.
    /// </summary>
    public static async Task<int> SeedAsync(
        AppDbContext db, ILogger? logger = null, CancellationToken ct = default)
    {
        var candidates = await db.Ingredients
            .Where(i => i.GramsPerMillilitre == null)
            .ToListAsync(ct);

        var updated = 0;
        foreach (var ingredient in candidates)
        {
            if (!TryFindDensity(ingredient, out var density)) continue;
            ingredient.GramsPerMillilitre = density;
            updated++;
        }

        if (updated > 0) await db.SaveChangesAsync(ct);

        logger?.LogInformation(
            "Density seed complete. Curated rows: {Rows}, catalogue names covered: {Names}, ingredients updated: {Updated}.",
            Rows.Count, ByCatalogueName.Count, updated);

        return updated;
    }

    /// <summary>
    /// Looks a density up by the ingredient's own name first, then by any of its aliases.
    ///
    /// <para><b>The alias arm is what keeps this working after Phase 9.3.</b> These 124 names were
    /// written against the old hand-written starter catalogue — the list said <c>plain flour</c>,
    /// which is why <c>all-purpose flour</c> carries it as a catalogue name. The corpus-derived
    /// catalogue renames the entries and folds the old spellings in as aliases, so a
    /// <c>Name</c>-only match would silently stop attaching densities to rows that used to get
    /// them.</para>
    ///
    /// <para>The name still wins where both match, so a curated figure keyed to the canonical entry
    /// is never displaced by one keyed to a spelling that entry merely answers to.</para>
    /// </summary>
    private static bool TryFindDensity(Models.Ingredient ingredient, out decimal density)
    {
        if (ByCatalogueName.TryGetValue(ingredient.Name, out density)) return true;

        foreach (var alias in ingredient.Aliases)
            if (ByCatalogueName.TryGetValue(alias, out density)) return true;

        density = default;
        return false;
    }
}
