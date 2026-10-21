using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RecipeApp.API.Services.Seeding;
using RecipeApp.Tests.Infrastructure;

namespace RecipeApp.Tests.Seeding;

/// <summary>
/// The shipped <c>seed-data/ingredient-catalogue.json</c> as an artefact (Phase 10 §4.7, §6): its
/// format-2 shape, rules 1–4, the loader's version handling, and — only where the local MyPlate
/// cache exists — rule 5. No database.
/// </summary>
public class ShippedCatalogueTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "shipped-catalogue-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_scratch)) Directory.Delete(_scratch, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static IngredientCatalogueFileStore StoreFor(string path, string contentRoot) => new(
        Options.Create(new RecipeSeedingOptions { CatalogueFilePath = path }),
        new TestHostEnvironment(contentRoot));

    private static Task<IngredientCatalogueFile> LoadShippedAsync() =>
        StoreFor(RepositoryPaths.ShippedCataloguePath, RepositoryPaths.ApiContentRoot).LoadAsync();

    // ── Shape (D8) ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadsAsFormatTwoAndPassesRulesOneToFour()
    {
        var catalogue = await LoadShippedAsync();

        catalogue.FormatVersion.Should().Be(IngredientCatalogueFile.CurrentFormatVersion);
        catalogue.ContentVersion.Should().BePositive();
        catalogue.Provenance.Should().Be(CatalogueProvenance.Curated);
        catalogue.Entries.Should().NotBeEmpty();
        catalogue.Entries.Select(entry => entry.Name).Should().BeInAscendingOrder(StringComparer.Ordinal);
        IngredientCatalogueValidator.Validate(catalogue.Entries).Should().BeEmpty();
    }

    [Fact]
    public async Task CarriesNoMyPlateProvenance()
    {
        var json = JsonNode.Parse(await File.ReadAllTextAsync(RepositoryPaths.ShippedCataloguePath))!.AsObject();

        json.Select(property => property.Key).Should().NotContain(["source", "version", "generatedAt"]);
        json["entries"]!.AsArray()
            .SelectMany(entry => entry!.AsObject().Select(property => property.Key))
            .Should().NotContain(["corpusRows", "corpusRecipes"]);
    }

    // ── Loader versions ───────────────────────────────────────────────────────

    private async Task<IngredientCatalogueFileStore> WriteScratchAsync(int formatVersion, int contentVersion)
    {
        var path = Path.Combine(_scratch, "ingredient-catalogue.json");
        Directory.CreateDirectory(_scratch);
        await File.WriteAllTextAsync(path,
            $$"""
            {
              "formatVersion": {{formatVersion}},
              "contentVersion": {{contentVersion}},
              "updatedAt": "2026-10-10T00:00:00Z",
              "provenance": "curated",
              "entries": [ { "name": "onion", "displayName": "Onion", "category": "PRODUCE", "defaultUnit": "pcs", "aliases": [] } ]
            }
            """);
        return StoreFor(path, _scratch);
    }

    [Fact]
    public async Task Loader_RejectsAnUnknownFormatVersion()
    {
        var store = await WriteScratchAsync(formatVersion: 3, contentVersion: 1);

        await store.Invoking(s => s.LoadAsync()).Should().ThrowAsync<CatalogueValidationException>()
            .WithMessage("*format version 3*");
    }

    [Fact]
    public async Task Loader_RejectsTheVersionOneShape()
    {
        var path = Path.Combine(_scratch, "ingredient-catalogue.json");
        Directory.CreateDirectory(_scratch);
        await File.WriteAllTextAsync(path,
            """{ "version": 1, "generatedAt": "2026-09-20T00:00:00Z", "source": { "corpus": "myplate" }, "entries": [] }""");

        await StoreFor(path, _scratch).Invoking(s => s.LoadAsync()).Should()
            .ThrowAsync<CatalogueValidationException>().WithMessage("*format version (none)*");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(250)]
    public async Task Loader_AcceptsAnyContentVersion(int contentVersion)
    {
        var store = await WriteScratchAsync(IngredientCatalogueFile.CurrentFormatVersion, contentVersion);

        (await store.LoadAsync()).ContentVersion.Should().Be(contentVersion);
    }

    // ── Row overrides point at real entries ───────────────────────────────────

    [Fact]
    public async Task EveryRowOverrideTargetIsAnEntryName()
    {
        // An entry name, not an alias: an alias can move to another entry in a later curation pass.
        var names = (await LoadShippedAsync()).Entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);

        SeedIngredientRowOverrides.BySlug.Values
            .SelectMany(list => list)
            .Select(entry => entry.CatalogueName)
            .Distinct()
            .Should().OnlyContain(target => names.Contains(target));
    }

    // ── Rule 5 (local only) ───────────────────────────────────────────────────

    /// <summary>
    /// Every ingredient row <c>--persist</c> would resolve still resolves (D9). Runs the same
    /// preparation as the persister — headings removed, then row overrides — and looks each name up
    /// by entry name, then alias. Skipped, not passed, where the gitignored cache is absent.
    /// </summary>
    [Fact]
    public async Task Rule5_EveryLocalCorpusRowResolvesAgainstTheShippedCatalogue()
    {
        var cache = new SeedCacheStore(
            Options.Create(new RecipeSeedingOptions { CacheDirectory = RepositoryPaths.MyPlateCacheDirectory }),
            new TestHostEnvironment(RepositoryPaths.ApiContentRoot),
            NullLogger<SeedCacheStore>.Instance);

        Assert.SkipUnless(Directory.Exists(cache.NormalisedDirectory),
            $"No local MyPlate cache at {cache.NormalisedDirectory}; rule 5 needs the normalised corpus.");

        var catalogue = await LoadShippedAsync();
        var reachable = catalogue.Entries
            .SelectMany(entry => entry.Aliases.Prepend(entry.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unresolved   = new List<string>();
        var staleListing = new List<string>();
        var slugs = Directory.EnumerateFiles(cache.NormalisedDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(slug => slug, StringComparer.Ordinal)
            .ToList();

        foreach (var slug in slugs)
        {
            var recipe = await cache.TryLoadNormalisedAsync(slug!);
            if (recipe is null) continue;

            var overridden = SeedIngredientRowOverrides.Apply(SeedNonIngredientRows.Remove(recipe).Recipe);
            staleListing.AddRange(overridden.Unmatched.Select(line => $"{slug}: {line}"));

            unresolved.AddRange(overridden.Recipe.Ingredients
                .Select(row => row.Name.Trim().ToLowerInvariant())
                .Where(name => !reachable.Contains(name))
                .Select(name => $"{slug}: {name}"));
        }

        slugs.Should().NotBeEmpty();
        unresolved.Should().BeEmpty("every MyPlate row must still resolve (Phase 10 D9)");
        staleListing.Should().BeEmpty("every row override must match a row in its recipe");
    }
}
