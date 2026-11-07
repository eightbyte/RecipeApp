using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeApp.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlannedMealServings : Migration
    {
        private const string PlannedMeals = "PlannedMeals";
        private const string ItemSources = "ShoppingListItemSources";
        private const string BatchSizeColumn = "BatchSize";
        private const string ServingsColumn = "Servings";
        private const string RegularBatchSize = "REGULAR";

        // The multipliers are spelled out here so that this conversion never changes with later code.
        private const string ServingsFromBatchSize = @"
            LEAST(100, CEIL(r.""Servings"" * CASE t.""BatchSize""
                WHEN 'HALF'   THEN 0.5
                WHEN 'DOUBLE' THEN 2.0
                ELSE 1.0
            END))";

        // The nearest Batch Size to the planned share of the recipe, since anything between them cannot go back.
        private const string BatchSizeFromServings = @"
            CASE
                WHEN t.""Servings""::numeric / r.""Servings"" <= 0.75 THEN 'HALF'
                WHEN t.""Servings""::numeric / r.""Servings"" >= 1.5  THEN 'DOUBLE'
                ELSE 'REGULAR'
            END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { PlannedMeals, ItemSources })
            {
                migrationBuilder.AddColumn<int>(name: ServingsColumn, table: table, type: "integer", nullable: true);
                migrationBuilder.Sql($@"
                    UPDATE ""{table}"" t SET ""Servings"" = {ServingsFromBatchSize}
                    FROM ""Recipes"" r WHERE r.""Id"" = t.""RecipeId"";");
                migrationBuilder.DropColumn(name: BatchSizeColumn, table: table);
            }

            migrationBuilder.AlterColumn<int>(
                name: ServingsColumn, table: PlannedMeals, type: "integer", nullable: false,
                oldClrType: typeof(int), oldType: "integer", oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { PlannedMeals, ItemSources })
            {
                migrationBuilder.AddColumn<string>(name: BatchSizeColumn, table: table, type: "text", nullable: true);
                migrationBuilder.Sql($@"
                    UPDATE ""{table}"" t SET ""BatchSize"" = {BatchSizeFromServings}
                    FROM ""Recipes"" r WHERE r.""Id"" = t.""RecipeId"";");
                migrationBuilder.Sql($@"UPDATE ""{table}"" SET ""BatchSize"" = '{RegularBatchSize}' WHERE ""BatchSize"" IS NULL;");
                migrationBuilder.DropColumn(name: ServingsColumn, table: table);
            }

            migrationBuilder.AlterColumn<string>(
                name: BatchSizeColumn, table: PlannedMeals, type: "text", nullable: false, defaultValue: RegularBatchSize,
                oldClrType: typeof(string), oldType: "text", oldNullable: true);
            migrationBuilder.AlterColumn<string>(
                name: BatchSizeColumn, table: ItemSources, type: "text", nullable: false,
                oldClrType: typeof(string), oldType: "text", oldNullable: true);
        }
    }
}
