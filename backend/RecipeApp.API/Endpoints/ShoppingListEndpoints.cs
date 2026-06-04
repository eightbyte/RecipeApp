using FluentValidation;
using RecipeApp.API.DTOs.ShoppingLists;
using RecipeApp.API.Services;

namespace RecipeApp.API.Endpoints;

public static class ShoppingListEndpoints
{
    public static IEndpointRouteBuilder MapShoppingListEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/shopping-lists").WithTags("Shopping Lists");

        // GET /shopping-lists/active — auto-generates if no list exists
        group.MapGet("/active", async (ShoppingListService svc) =>
        {
            var list = await svc.GetActiveAsync();
            return list is null ? Results.NotFound() : Results.Ok(list);
        })
        .WithSummary("Get the shopping list for the active meal plan")
        .WithDescription("Auto-generates the list from recipe ingredients if none exists. Returns 404 if there is no active meal plan.");

        // GET /shopping-lists/{id}
        group.MapGet("/{id:guid}", async (Guid id, ShoppingListService svc) =>
        {
            var list = await svc.GetByIdAsync(id);
            return list is null ? Results.NotFound() : Results.Ok(list);
        })
        .WithSummary("Get a shopping list by id");

        // POST /shopping-lists/active/generate — force-regenerate; preserves custom items
        group.MapPost("/active/generate", async (ShoppingListService svc) =>
        {
            var list = await svc.RegenerateActiveAsync();
            return list is null ? Results.NotFound() : Results.Ok(list);
        })
        .WithSummary("Regenerate the shopping list for the active meal plan")
        .WithDescription("Replaces all recipe-derived items with fresh aggregated data; custom items are preserved. Returns 404 if there is no active meal plan.");

        // POST /shopping-lists/{id}/items — add a custom item
        group.MapPost("/{id:guid}/items", async (
            Guid id,
            AddCustomItemRequest request,
            ShoppingListService svc,
            IValidator<AddCustomItemRequest> validator) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var item = await svc.AddCustomItemAsync(id, request);
            return item is null
                ? Results.NotFound()
                : Results.Created($"/api/v1/shopping-lists/{id}", item);
        })
        .WithSummary("Add a custom item to a shopping list")
        .WithDescription("Custom items are preserved when the list is regenerated from the meal plan.");

        // PUT /shopping-lists/{id}/items/{itemId} — toggle checked, edit qty
        group.MapPut("/{id:guid}/items/{itemId:guid}", async (
            Guid id,
            Guid itemId,
            UpdateItemRequest request,
            ShoppingListService svc,
            IValidator<UpdateItemRequest> validator) =>
        {
            var validation = await validator.ValidateAsync(request);
            if (!validation.IsValid)
                return Results.ValidationProblem(validation.ToDictionary());

            var item = await svc.UpdateItemAsync(id, itemId, request);
            return item is null ? Results.NotFound() : Results.Ok(item);
        })
        .WithSummary("Update a shopping list item")
        .WithDescription("Toggle the checked state or edit the quantity/unit of any item.");

        // DELETE /shopping-lists/{id}/items/{itemId} — custom items only
        group.MapDelete("/{id:guid}/items/{itemId:guid}", async (
            Guid id,
            Guid itemId,
            ShoppingListService svc) =>
        {
            var result = await svc.DeleteItemAsync(id, itemId);
            return result switch
            {
                DeleteItemResult.Deleted   => Results.NoContent(),
                DeleteItemResult.NotFound  => Results.NotFound(),
                DeleteItemResult.Forbidden => Results.Conflict(new
                {
                    title  = "Cannot delete recipe-derived item.",
                    detail = "Recipe-derived items are removed by editing the meal plan and regenerating the shopping list.",
                }),
                _ => Results.StatusCode(500),
            };
        })
        .WithSummary("Delete a custom shopping list item")
        .WithDescription("Only custom (user-added) items may be deleted. Recipe-derived items return 409 Conflict.");

        return routes;
    }
}
