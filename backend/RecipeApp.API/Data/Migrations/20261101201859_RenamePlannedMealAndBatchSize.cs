using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeApp.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePlannedMealAndBatchSize : Migration
    {
        private const string OldTable = "MealPlanRecipes";
        private const string NewTable = "PlannedMeals";
        private const string OldBatchSizeColumn = "PortionSize";
        private const string NewBatchSizeColumn = "BatchSize";

        private static readonly (string Old, string New)[] ConstraintNames =
        [
            ("PK_MealPlanRecipes", "PK_PlannedMeals"),
            ("FK_MealPlanRecipes_MealPlans_MealPlanId", "FK_PlannedMeals_MealPlans_MealPlanId"),
            ("FK_MealPlanRecipes_Recipes_RecipeId", "FK_PlannedMeals_Recipes_RecipeId"),
        ];

        private static readonly (string Old, string New)[] IndexNames =
        [
            ("IX_MealPlanRecipes_MealPlanId", "IX_PlannedMeals_MealPlanId"),
            ("IX_MealPlanRecipes_RecipeId", "IX_PlannedMeals_RecipeId"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: OldTable, newName: NewTable);
            migrationBuilder.RenameColumn(name: OldBatchSizeColumn, table: NewTable, newName: NewBatchSizeColumn);

            foreach (var (oldName, newName) in ConstraintNames)
                RenameConstraint(migrationBuilder, NewTable, oldName, newName);

            foreach (var (oldName, newName) in IndexNames)
                migrationBuilder.RenameIndex(name: oldName, newName: newName, table: NewTable);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (oldName, newName) in IndexNames)
                migrationBuilder.RenameIndex(name: newName, newName: oldName, table: NewTable);

            foreach (var (oldName, newName) in ConstraintNames)
                RenameConstraint(migrationBuilder, NewTable, newName, oldName);

            migrationBuilder.RenameColumn(name: NewBatchSizeColumn, table: NewTable, newName: OldBatchSizeColumn);
            migrationBuilder.RenameTable(name: NewTable, newName: OldTable);
        }

        // EF Core has no builder operation for renaming a key, and dropping one to re-add it would briefly unprotect the rows.
        private static void RenameConstraint(MigrationBuilder migrationBuilder, string table, string oldName, string newName) =>
            migrationBuilder.Sql($@"ALTER TABLE ""{table}"" RENAME CONSTRAINT ""{oldName}"" TO ""{newName}"";");
    }
}
