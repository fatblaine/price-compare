using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace PriceCompareData.Migrations
{
    /// <inheritdoc />
    public partial class AddMappingMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryId);
                });

            migrationBuilder.CreateTable(
                name: "CategoryKeywords",
                columns: table => new
                {
                    KeywordId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    Keyword = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Weight = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CategoryKeywords", x => x.KeywordId);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShopType = table.Column<int>(type: "int", nullable: true),
                    SourceId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SizeValue = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    SizeUnit = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PackageQty = table.Column<int>(type: "int", nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastSeenAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.ProductId);
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "CategoryId", "Name", "ParentId" },
                values: new object[,]
                {
                    { 1, "Milk", null },
                    { 2, "Bread", null },
                    { 3, "Eggs", null },
                    { 4, "Apple", null },
                    { 5, "Toothpaste", null },
                    { 6, "Rice", null },
                    { 7, "Pasta", null },
                    { 8, "Shampoo", null },
                    { 9, "Soap", null },
                    { 10, "Cheese", null },
                    { 11, "Chicken", null },
                    { 12, "Beef", null },
                    { 13, "Fish", null },
                    { 14, "Yogurt", null },
                    { 15, "Coffee", null },
                    { 16, "Tea", null },
                    { 17, "Soft Drink", null },
                    { 18, "Beer", null },
                    { 19, "Wine", null },
                    { 20, "Cereal", null }
                });

            migrationBuilder.InsertData(
                table: "CategoryKeywords",
                columns: new[] { "KeywordId", "CategoryId", "Keyword", "Weight" },
                values: new object[,]
                {
                    { 1, 1, "milk", 1 },
                    { 2, 1, "full cream", 1 },
                    { 3, 1, "skim", 1 },
                    { 4, 1, "lactose free", 1 },
                    { 10, 2, "bread", 1 },
                    { 11, 2, "wholemeal", 1 },
                    { 12, 2, "sourdough", 1 },
                    { 20, 3, "egg", 1 },
                    { 21, 3, "free range", 1 },
                    { 30, 4, "apple", 1 },
                    { 31, 4, "fuji", 1 },
                    { 32, 4, "gala", 1 },
                    { 40, 5, "toothpaste", 1 },
                    { 41, 5, "whitening", 1 },
                    { 42, 5, "fluoride", 1 },
                    { 50, 6, "rice", 1 },
                    { 51, 6, "basmati", 1 },
                    { 52, 6, "jasmine", 1 },
                    { 60, 7, "pasta", 1 },
                    { 61, 7, "spaghetti", 1 },
                    { 62, 7, "penne", 1 },
                    { 70, 8, "shampoo", 1 },
                    { 71, 8, "anti-dandruff", 1 },
                    { 80, 9, "soap", 1 },
                    { 81, 9, "bar soap", 1 },
                    { 90, 10, "cheese", 1 },
                    { 91, 10, "cheddar", 1 },
                    { 92, 10, "mozzarella", 1 },
                    { 100, 11, "chicken", 1 },
                    { 101, 11, "breast fillet", 1 },
                    { 102, 11, "drumstick", 1 },
                    { 110, 12, "beef", 1 },
                    { 111, 12, "mince", 1 },
                    { 112, 12, "steak", 1 },
                    { 120, 13, "fish", 1 },
                    { 121, 13, "salmon", 1 },
                    { 122, 13, "tuna", 1 },
                    { 130, 14, "yogurt", 1 },
                    { 131, 14, "greek", 1 },
                    { 140, 15, "coffee", 1 },
                    { 141, 15, "instant", 1 },
                    { 142, 15, "espresso", 1 },
                    { 150, 16, "tea", 1 },
                    { 151, 16, "green tea", 1 },
                    { 152, 16, "black tea", 1 },
                    { 160, 17, "cola", 1 },
                    { 161, 17, "soft drink", 1 },
                    { 162, 17, "soda", 1 },
                    { 170, 18, "beer", 1 },
                    { 171, 18, "lager", 1 },
                    { 172, 18, "ale", 1 },
                    { 180, 19, "wine", 1 },
                    { 181, 19, "red wine", 1 },
                    { 182, 19, "white wine", 1 },
                    { 190, 20, "cereal", 1 },
                    { 191, 20, "cornflakes", 1 },
                    { 192, 20, "muesli", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Products_CategoryId_SizeUnit_SizeValue",
                table: "Products",
                columns: new[] { "CategoryId", "SizeUnit", "SizeValue" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ShopType_Name",
                table: "Products",
                columns: new[] { "ShopType", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ShopType_SourceId",
                table: "Products",
                columns: new[] { "ShopType", "SourceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "CategoryKeywords");

            migrationBuilder.DropTable(
                name: "Products");
        }
    }
}
