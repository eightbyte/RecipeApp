using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeApp.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIngredientStaple : Migration
    {
        // Frozen here rather than read from the catalogue, so later catalogue edits never change what this migration did.
        private static readonly string[] InitialStapleNames = ["salt", "black pepper", "water"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsStaple",
                table: "Ingredients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            var quotedNames = string.Join(", ", InitialStapleNames.Select(name => $"'{name}'"));
            migrationBuilder.Sql($@"UPDATE ""Ingredients"" SET ""IsStaple"" = true WHERE ""Name"" IN ({quotedNames});");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsStaple",
                table: "Ingredients");
        }
    }
}
