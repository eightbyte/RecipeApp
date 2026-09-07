using RecipeApp.API.DTOs.MealPlans;
using RecipeApp.API.Filters;
using RecipeApp.API.Services;

namespace RecipeApp.API.Endpoints;

public static class MealPlanEndpoints
{
    public static IEndpointRouteBuilder MapMealPlanEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/meal-plans").WithTags("Meal Plans");

        // GET /meal-plans
        group.MapGet("/", async (MealPlanService svc) =>
            Results.Ok(await svc.GetListAsync())
        )
        .WithSummary("List all meal plans")
        .WithDescription("Returns all plans ordered: active first, then by creation date descending.");

        // GET /meal-plans/active  — must be before /{id:guid}
        group.MapGet("/active", async (MealPlanService svc) =>
        {
            var plan = await svc.GetActiveAsync();
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        })
        .WithSummary("Get the current active meal plan with its recipes");

        // GET /meal-plans/{id}
        group.MapGet("/{id:guid}", async (Guid id, MealPlanService svc) =>
        {
            var plan = await svc.GetByIdAsync(id);
            return plan is null ? Results.NotFound() : Results.Ok(plan);
        })
        .WithSummary("Get a meal plan by id");

        // GET /meal-plans/{id}/suggestions
        group.MapGet("/{id:guid}/suggestions", async (Guid id, MealPlanService svc) =>
        {
            var suggestions = await svc.GetSuggestionsAsync(id);
            return suggestions is null ? Results.NotFound() : Results.Ok(suggestions);
        })
        .WithSummary("Get waste-reduction recipe suggestions for a meal plan")
        .WithDescription("Returns recipes not already in the plan that share ingredients with it, ranked by overlap count.");

        // POST /meal-plans
        group.MapPost("/", async (
            CreateMealPlanRequest request,
            MealPlanService svc) =>
        {
            var created = await svc.CreateAsync(request);
            return Results.Created($"/api/v1/meal-plans/{created.Id}", created);
        })
        .WithValidation<CreateMealPlanRequest>()
        .WithSummary("Create a new meal plan")
        .WithDescription("Deactivates the current active plan, then creates the new plan as active.");

        // PUT /meal-plans/{id}
        group.MapPut("/{id:guid}", async (
            Guid id,
            UpdateMealPlanRequest request,
            MealPlanService svc) =>
        {
            var updated = await svc.UpdateAsync(id, request);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithValidation<UpdateMealPlanRequest>()
        .WithSummary("Rename a meal plan");

        // DELETE /meal-plans/{id}
        group.MapDelete("/{id:guid}", async (Guid id, MealPlanService svc) =>
        {
            var deleted = await svc.DeleteAsync(id);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Delete a meal plan and its recipes");

        // POST /meal-plans/{id}/recipes
        group.MapPost("/{id:guid}/recipes", async (
            Guid id,
            AddMealPlanRecipeRequest request,
            MealPlanService svc) =>
        {
            var added = await svc.AddRecipeAsync(id, request);
            return added is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/meal-plans/{id}/recipes/{added.Id}", added);
        })
        .WithValidation<AddMealPlanRecipeRequest>()
        .WithSummary("Add a recipe to a meal plan");

        // PUT /meal-plans/{id}/recipes/{mprId}
        group.MapPut("/{id:guid}/recipes/{mprId:guid}", async (
            Guid id,
            Guid mprId,
            UpdateMealPlanRecipeRequest request,
            MealPlanService svc) =>
        {
            var updated = await svc.UpdateRecipeAsync(id, mprId, request);
            return updated is null ? Results.NotFound() : Results.Ok(updated);
        })
        .WithValidation<UpdateMealPlanRecipeRequest>()
        .WithSummary("Update the scheduled date or portion size of a plan recipe");

        // DELETE /meal-plans/{id}/recipes/{mprId}
        group.MapDelete("/{id:guid}/recipes/{mprId:guid}", async (
            Guid id,
            Guid mprId,
            MealPlanService svc) =>
        {
            var removed = await svc.RemoveRecipeAsync(id, mprId);
            return removed ? Results.NoContent() : Results.NotFound();
        })
        .WithSummary("Remove a recipe from a meal plan");

        return routes;
    }
}
