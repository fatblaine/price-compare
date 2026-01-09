using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceCompareData.Migrations
{
    /// <inheritdoc />
    public partial class AddShopTypeToPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ShopType",
                table: "PriceHistory",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShopType",
                table: "PriceHistory");
        }
    }
}
