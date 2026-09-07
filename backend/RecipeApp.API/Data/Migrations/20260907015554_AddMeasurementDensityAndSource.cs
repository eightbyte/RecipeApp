using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeApp.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMeasurementDensityAndSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "SourceAmount",
                table: "RecipeIngredients",
                type: "numeric(10,3)",
                precision: 10,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SourceUnit",
                table: "RecipeIngredients",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "GramsPerMillilitre",
                table: "Ingredients",
                type: "numeric(8,4)",
                precision: 8,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceAmount",
                table: "RecipeIngredients");

            migrationBuilder.DropColumn(
                name: "SourceUnit",
                table: "RecipeIngredients");

            migrationBuilder.DropColumn(
                name: "GramsPerMillilitre",
                table: "Ingredients");
        }
    }
}
