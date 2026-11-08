using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.Tests.Models;

/// <summary>Every persistence path stores its measurement through <see cref="RecipeIngredient.ToStoredMeasurement"/>, so its pairing rules are pinned here.</summary>
public class RecipeIngredientTests
{
    [Fact]
    public void ToStoredMeasurement_KeepsAStatedUnit()
    {
        RecipeIngredient.ToStoredMeasurement(100m, " g ").Should().Be((100m, "g"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ToStoredMeasurement_AnAmountWithNoUnit_IsACount(string? unit)
    {
        RecipeIngredient.ToStoredMeasurement(2m, unit).Should().Be((2m, MeasurementUnit.Piece));
    }

    [Fact]
    public void ToStoredMeasurement_NoAmount_DropsTheUnit()
    {
        RecipeIngredient.ToStoredMeasurement(null, "g").Should().Be(((decimal?)null, (string?)null));
    }
}
