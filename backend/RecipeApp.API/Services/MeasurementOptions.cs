namespace RecipeApp.API.Services;

public class MeasurementOptions
{
    public const string SectionName = "Measurement";

    /// <summary>When false, import-time cup resolution is skipped and imports keep <c>cup</c> as stated regardless of density.</summary>
    public bool ResolveCupsOnImport { get; init; } = true;
}
