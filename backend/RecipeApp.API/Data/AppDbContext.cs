using Microsoft.EntityFrameworkCore;
using RecipeApp.API.Models;

namespace RecipeApp.API.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Ingredient> Ingredients => Set<Ingredient>();
    public DbSet<Recipe> Recipes => Set<Recipe>();
    public DbSet<RecipeIngredient> RecipeIngredients => Set<RecipeIngredient>();
    public DbSet<RecipeStep> RecipeSteps => Set<RecipeStep>();
    public DbSet<RecipeStepIngredient> RecipeStepIngredients => Set<RecipeStepIngredient>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Ingredient ────────────────────────────────────────────────────────
        modelBuilder.Entity<Ingredient>(e =>
        {
            e.HasIndex(i => i.Name).IsUnique();
            e.Property(i => i.Category).HasDefaultValue("OTHER");
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
    }
}
