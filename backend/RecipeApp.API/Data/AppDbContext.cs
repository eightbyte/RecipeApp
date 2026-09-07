using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Enums;
using RecipeApp.API.Models;

namespace RecipeApp.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<RecipeStepIngredient> RecipeStepIngredients => Set<RecipeStepIngredient>();
    public DbSet<MealPlan> MealPlans => Set<MealPlan>();
    public DbSet<MealPlanRecipe> MealPlanRecipes => Set<MealPlanRecipe>();
    public DbSet<ShoppingList> ShoppingLists => Set<ShoppingList>();
    public DbSet<ShoppingListItem> ShoppingListItems => Set<ShoppingListItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Ingredient ────────────────────────────────────────────────────────
        modelBuilder.Entity<Ingredient>(e =>
        {
            e.HasIndex(i => i.Name).IsUnique();
            e.Property(i => i.Category).HasDefaultValue("OTHER");
            e.Property(i => i.GramsPerMillilitre).HasPrecision(8, 4);
            e.Property(i => i.CreatedAt).HasDefaultValueSql("NOW()");
        });

        // ── Recipe ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Recipe>(e =>
        {
            e.Property(r => r.Servings).HasDefaultValue(4);
            e.Property(r => r.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(r => r.UpdatedAt).HasDefaultValueSql("NOW()");
        });

        // ── RecipeIngredient ──────────────────────────────────────────────────
        modelBuilder.Entity<RecipeIngredient>(e =>
        {
            e.Property(ri => ri.Amount).HasPrecision(10, 3);
            e.Property(ri => ri.SourceAmount).HasPrecision(10, 3);
            e.Property(ri => ri.SourceUnit).HasMaxLength(32);

            e.HasOne(ri => ri.Recipe)
                .WithMany(r => r.Ingredients)
                .HasForeignKey(ri => ri.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ri => ri.Ingredient)
                .WithMany(i => i.RecipeIngredients)
                .HasForeignKey(ri => ri.IngredientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── RecipeStep ────────────────────────────────────────────────────────
        modelBuilder.Entity<RecipeStep>(e =>
        {
            e.HasOne(rs => rs.Recipe)
                .WithMany(r => r.Steps)
                .HasForeignKey(rs => rs.RecipeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── RecipeStepIngredient (composite PK) ───────────────────────────────
        modelBuilder.Entity<RecipeStepIngredient>(e =>
        {
            e.HasKey(rsi => new { rsi.StepId, rsi.RecipeIngredientId });

            e.HasOne(rsi => rsi.Step)
                .WithMany(rs => rs.StepIngredients)
                .HasForeignKey(rsi => rsi.StepId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(rsi => rsi.RecipeIngredient)
                .WithMany(ri => ri.StepIngredients)
                .HasForeignKey(rsi => rsi.RecipeIngredientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── MealPlan ──────────────────────────────────────────────────────────
        modelBuilder.Entity<MealPlan>(e =>
        {
            e.Property(p => p.CreatedAt).HasDefaultValueSql("NOW()");
            e.Property(p => p.UpdatedAt).HasDefaultValueSql("NOW()");

            // Enforce a single active plan via a partial unique index.
            e.HasIndex(p => p.IsActive)
                .IsUnique()
                .HasFilter("\"IsActive\" = true");
        });

        // ── MealPlanRecipe ────────────────────────────────────────────────────
        modelBuilder.Entity<MealPlanRecipe>(e =>
        {
            e.Property(mpr => mpr.PortionSize).HasDefaultValue(PortionSize.Regular);

            e.HasOne(mpr => mpr.MealPlan)
                .WithMany(p => p.Recipes)
                .HasForeignKey(mpr => mpr.MealPlanId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(mpr => mpr.Recipe)
                .WithMany()
                .HasForeignKey(mpr => mpr.RecipeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── ShoppingList ──────────────────────────────────────────────────────
        modelBuilder.Entity<ShoppingList>(e =>
        {
            e.Property(s => s.GeneratedAt).HasDefaultValueSql("NOW()");
            e.Property(s => s.UpdatedAt).HasDefaultValueSql("NOW()");

            e.HasIndex(s => s.MealPlanId).IsUnique();

            e.HasOne(s => s.MealPlan)
                .WithMany()
                .HasForeignKey(s => s.MealPlanId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── ShoppingListItem ──────────────────────────────────────────────────
        modelBuilder.Entity<ShoppingListItem>(e =>
        {
            e.Property(i => i.Amount).HasPrecision(10, 3);
            e.Property(i => i.Category).HasDefaultValue(IngredientCategory.Other);

            e.HasOne(i => i.ShoppingList)
                .WithMany(s => s.Items)
                .HasForeignKey(i => i.ShoppingListId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(i => i.Ingredient)
                .WithMany()
                .HasForeignKey(i => i.IngredientId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
