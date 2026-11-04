using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RecipeApp.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShoppingListItemSources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShoppingListItemSources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ShoppingListItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecipeName = table.Column<string>(type: "text", nullable: false),
                    BatchSize = table.Column<string>(type: "text", nullable: false),
                    ScheduledDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    Unit = table.Column<string>(type: "text", nullable: true),
                    ShareAmount = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: true),
                    ShareUnit = table.Column<string>(type: "text", nullable: true),
                    DisplayOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingListItemSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingListItemSources_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ShoppingListItemSources_ShoppingListItems_ShoppingListItemId",
                        column: x => x.ShoppingListItemId,
                        principalTable: "ShoppingListItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingListItemSources_RecipeId",
                table: "ShoppingListItemSources",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingListItemSources_ShoppingListItemId",
                table: "ShoppingListItemSources",
                column: "ShoppingListItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShoppingListItemSources");
        }
    }
}
