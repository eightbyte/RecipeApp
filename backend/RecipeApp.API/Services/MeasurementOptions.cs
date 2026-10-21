namespace RecipeApp.API.Services;

public class MeasurementOptions
{
    public const string SectionName = "Measurement";

    /// <summary>
    /// When false, import-time cup resolution (Stage B) is skipped entirely and imports keep
    /// <c>cup</c> as stated regardless of density. Escape hatch for a seeding trial run that
    /// needs to inspect raw conversions.
    /// </summary>
    public bool ResolveCupsOnImport { get; init; } = true;
}
