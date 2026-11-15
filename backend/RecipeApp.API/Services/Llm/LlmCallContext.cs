using System.Text.Json.Serialization;

namespace RecipeApp.API.Services.Llm;

/// <summary>Who made an LLM Call and for what, recorded with it so related calls can be read together.</summary>
/// <param name="Step">Which call within a Scrape, or null for a seed call, which makes only one kind.</param>
/// <param name="SourceUrl">The Scrape's URL when it has one, never pasted text, which may be private.</param>
/// <param name="CorrelationId">Shared by every call for one item: a Scrape, a MyPlate recipe or a catalogue batch, retries included.</param>
public sealed record LlmCallContext(
    LlmOperation Operation,
    LlmStep? Step,
    string? SourceUrl,
    Guid CorrelationId);

[JsonConverter(typeof(JsonStringEnumConverter<LlmOperation>))]
public enum LlmOperation
{
    ScrapeUrl,
    ScrapeText,
    SeedNormalise,
    CatalogueBuild,
}

[JsonConverter(typeof(JsonStringEnumConverter<LlmStep>))]
public enum LlmStep
{
    Extraction,
    IngredientMatching,
}
