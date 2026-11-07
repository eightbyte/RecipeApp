using RecipeApp.API.Services.Llm;

namespace RecipeApp.API.DTOs.ModelServer;

/// <param name="Model">The loaded model's name, the start script's <c>--alias</c>, when the server is ready and reports one.</param>
public record ModelServerStatusResponse(ModelServerState State, string? Model);
