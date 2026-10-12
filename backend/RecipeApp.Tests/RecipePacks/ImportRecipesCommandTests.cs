using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using RecipeApp.API.Services.RecipePacks;

namespace RecipeApp.Tests.RecipePacks;

/// <summary>
/// The <c>import-recipes</c> CLI's own argument vector (Phase 10 §4.4.3). Pure functions.
/// The command against a database is in <see cref="ImportRecipesCommandRunTests"/>.
/// </summary>
public class ImportRecipesCommandTests
{
    private static ImportRecipesCommand.ImportRecipesArguments? Parse(params string[] args) =>
        ImportRecipesCommand.TryParse(args, NullLogger.Instance);

    [Fact]
    public void APathAloneImportsWithNeitherFlag()
    {
        var arguments = Parse("pack.json")!;

        arguments.PackPath.Should().Be("pack.json");
        arguments.Settings.Should().Be(new RecipePackImportSettings(CreateMissingIngredients: false, ReplaceExisting: false));
    }

    [Fact]
    public void BothFlagsAreReadInAnyPosition()
    {
        var arguments = Parse("--force", "pack.json", "--create-missing")!;

        arguments.PackPath.Should().Be("pack.json");
        arguments.Settings.CreateMissingIngredients.Should().BeTrue();
        arguments.Settings.ReplaceExisting.Should().BeTrue();
    }

    [Fact]
    public void NoPathIsAUsageError() => Parse("--force").Should().BeNull();

    [Fact]
    public void TwoPathsAreAUsageError() => Parse("a.json", "b.json").Should().BeNull();

    [Fact]
    public void AnUnknownFlagIsAUsageError() => Parse("pack.json", "--create-mising").Should().BeNull();

    [Fact]
    public void IndexInFindsTheCommandToken()
    {
        ImportRecipesCommand.IndexIn(["--urls", "x", "import-recipes", "pack.json"]).Should().Be(2);
        ImportRecipesCommand.IndexIn(["import-catalogue"]).Should().Be(-1);
    }
}
