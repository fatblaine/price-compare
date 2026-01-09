using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PriceCompareData.Migrations
{
    /// <inheritdoc />
    public partial class AddOfferTypeToPriceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "OfferType",
                table: "PriceHistory",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OfferType",
                table: "PriceHistory");
        }
    }
}
