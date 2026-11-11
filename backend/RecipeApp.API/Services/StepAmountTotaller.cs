using RecipeApp.API.Enums;

namespace RecipeApp.API.Services;

/// <summary>One amount a step states for an ingredient, as the step stated it.</summary>
public readonly record struct StepAmount(decimal? Amount, string? Unit);

/// <summary>What the amounts the steps state for one ingredient add up to.</summary>
public abstract record StepAmountTotal
{
    private StepAmountTotal() { }

    /// <summary>Every mention shares one unit, so the total in that unit is read like a stated ingredient line and kept as its Source Measurement.</summary>
    public sealed record InSharedUnit(decimal Amount, string? Unit) : StepAmountTotal;

    /// <summary>Every mention is in a Sizeless Unit, so the row stays an Unquantified Ingredient with the word as its Source Measurement.</summary>
    public sealed record Sizeless(decimal? SourceAmount, string Word) : StepAmountTotal;

    /// <summary>The mentions are in different units, so only the converted total is kept, with no Source Measurement.</summary>
    public sealed record Converted(decimal Amount, string Unit) : StepAmountTotal;

    /// <summary>The mentions cannot be added up honestly, such as a volume and a mass with no density, so the row stays empty.</summary>
    public sealed record NotAddedUp : StepAmountTotal;
}

/// <summary>Adds up the amounts the steps state for an ingredient whose list line states none, in code because the model cannot be trusted with arithmetic.</summary>
public static class StepAmountTotaller
{
    /// <param name="ingredientName">The scraped ingredient name, which decides whether a word such as <c>stick</c> is a Count Word.</param>
    /// <param name="gramsPerMillilitre">The matched Ingredient's density, or null when there is none.</param>
    /// <returns>Null when no mention states anything.</returns>
    public static StepAmountTotal? Total(
        IReadOnlyList<StepAmount> mentions, string ingredientName, decimal? gramsPerMillilitre)
    {
        var sizeless = mentions.Where(m => SizelessUnit.IsSizeless(m.Unit)).ToList();
        var sized    = mentions.Where(m => m.Amount is > 0m && !SizelessUnit.IsSizeless(m.Unit)).ToList();

        if (sized.Count == 0)
        {
            if (sizeless.Count == 0) return null;
            var word = sizeless[0].Unit!.Trim();
            return sizeless.Count == 1
                ? new StepAmountTotal.Sizeless(sizeless[0].Amount is > 0m ? sizeless[0].Amount : null, word)
                : new StepAmountTotal.Sizeless(null, word);
        }

        var total = sized.Sum(m => m.Amount!.Value);
        var units = sized.Select(m => SpellingOf(m.Unit)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (units.Count == 1) return new StepAmountTotal.InSharedUnit(total, units[0]);

        var inBase = sized.Select(m => InBaseUnit(m, ingredientName)).ToList();
        if (inBase.Any(m => m is null)) return new StepAmountTotal.NotAddedUp();

        var byDimension = inBase
            .GroupBy(m => m!.Value.Dimension)
            .ToDictionary(g => g.Key, g => g.Sum(m => m!.Value.Amount));

        if (byDimension.Count == 1)
        {
            var (dimension, amount) = byDimension.Single();
            return new StepAmountTotal.Converted(Math.Round(amount, 3), BaseUnitOf(dimension));
        }

        // The same rule as shopping-list consolidation: a density makes a volume and a mass one quantity.
        if (byDimension.Count == 2
            && gramsPerMillilitre is > 0m and { } density
            && byDimension.TryGetValue(UnitDimension.Mass, out var grams)
            && byDimension.TryGetValue(UnitDimension.Volume, out var millilitres))
            return new StepAmountTotal.Converted(
                Math.Round(grams + MeasurementConverter.MillilitresToGrams(millilitres, density), 3),
                MeasurementUnit.Gram);

        return new StepAmountTotal.NotAddedUp();
    }

    /// <summary>The unit as one spelling, so <c>teaspoon</c> and <c>tsp</c> are one unit; null for a bare count.</summary>
    private static string? SpellingOf(string? unit)
    {
        if (string.IsNullOrWhiteSpace(unit)) return null;
        return MeasurementUnit.TryCanonicalise(unit, out var canonical) ? canonical : unit.Trim();
    }

    /// <summary>A sized mention in its dimension's base unit, or null when its unit has no known size.</summary>
    private static (UnitDimension Dimension, decimal Amount)? InBaseUnit(StepAmount mention, string ingredientName)
    {
        var amount = mention.Amount!.Value;
        if (string.IsNullOrWhiteSpace(mention.Unit)
            || CountWords.IsCountWord(mention.Unit, ingredientName)
            || CountWords.IsSizeWord(mention.Unit))
            return (UnitDimension.Count, amount);

        var (canonicalAmount, canonicalUnit) = MeasurementConverter.ToCanonical((double)amount, mention.Unit);
        return MeasurementUnit.DimensionOf(canonicalUnit) is { } dimension
               && MeasurementUnit.ToBase(canonicalAmount, canonicalUnit) is { } baseAmount
            ? (dimension, baseAmount)
            : null;
    }

    private static string BaseUnitOf(UnitDimension dimension) => dimension switch
    {
        UnitDimension.Mass   => MeasurementUnit.Gram,
        UnitDimension.Volume => MeasurementUnit.Millilitre,
        _                    => MeasurementUnit.Piece,
    };
}
