using System;
using FluentAssertions;
using PriceCompareCore.Services;
using Xunit;

namespace PriceCompareTests
{
    // BTS-162: Woolworths now spells prices out in words in the link aria-label, so the WWS DOM
    // scrapers read name and prices from the product tile instead.
    public class WoolworthsTileReaderTests
    {
        [Theory]
        [InlineData("20.00", 20.00)]
        [InlineData("$8.00", 8.00)]
        [InlineData("$ 10.50", 10.50)]
        [InlineData("Was $7 - 10/11/2026", 7)]
        [InlineData("Was $21.00", 21.00)]
        [InlineData("$1,299.00", 1299.00)]
        public void ParseAmount_ReadsFirstDollarAmount(string raw, double expected)
        {
            WoolworthsTileReader.ParseAmount(raw).Should().Be((decimal)expected);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Was")]
        [InlineData("Price twenty dollars")]
        public void ParseAmount_ReturnsNullWithoutDigits(string? raw)
        {
            WoolworthsTileReader.ParseAmount(raw).Should().BeNull();
        }

        [Fact]
        public void FromRaw_ParsesHalfPriceTile()
        {
            var tile = WoolworthsTileReader.FromRaw(
                "Gillette Venus Satin Care Shaving Gel Sensitive Skin 195g", "4.00", "$8.00", null);

            tile.Should().NotBeNull();
            tile!.Name.Should().Be("Gillette Venus Satin Care Shaving Gel Sensitive Skin 195g");
            tile.Price.Should().Be(4.00m);
            tile.WasPrice.Should().Be(8.00m);
            tile.PromoText.Should().BeNull();
        }

        [Fact]
        public void FromRaw_KeepsMultiBuyText()
        {
            var tile = WoolworthsTileReader.FromRaw(" Nivea SPF 50 Sunscreen 400mL ", "28.00", null, " 2 for $45 ");

            tile!.Name.Should().Be("Nivea SPF 50 Sunscreen 400mL");
            tile.WasPrice.Should().BeNull();
            tile.PromoText.Should().Be("2 for $45");
        }

        [Fact]
        public void FromRaw_DropsWasPriceNotAbovePrice()
        {
            WoolworthsTileReader.FromRaw("Milk 2L", "3.10", "$3.10", null)!.WasPrice.Should().BeNull();
        }

        [Theory]
        [InlineData(null, "4.00")]
        [InlineData("", "4.00")]
        [InlineData("12", "4.00")]
        [InlineData("Milk 2L", null)]
        [InlineData("Milk 2L", "")]
        [InlineData("Milk 2L", "0.00")]
        public void FromRaw_ReturnsNullWithoutNameOrPrice(string? name, string? price)
        {
            WoolworthsTileReader.FromRaw(name, price, null, null).Should().BeNull();
        }

        [Fact]
        public void ToProduct_SetsSavingsFromWasPrice()
        {
            var tile = new WoolworthsTileData("Jif Cleaner 280mL", 5.25m, 10.50m, null);
            var scrapedAt = new DateTime(2026, 11, 14, 0, 0, 0, DateTimeKind.Utc);

            var product = WoolworthsTileReader.ToProduct(tile, null, scrapedAt, "autumn price");

            product.DisplayName.Should().Be("Jif Cleaner 280mL");
            product.Price.Should().Be(5.25m);
            product.WasPrice.Should().Be(10.50m);
            product.SavingsAmount.Should().Be(5.25m);
            product.LargeImageFile.Should().BeEmpty();
            product.PromoText.Should().Be("autumn price");
            product.IsOnSpecial.Should().BeTrue();
            product.ScrapedAt.Should().Be(scrapedAt);
        }

        [Fact]
        public void EnsureParsed_ThrowsWhenTilesFoundButNothingParsed()
        {
            var act = () => WoolworthsTileReader.EnsureParsed("everyday-low-price", true, 3000, 0);

            act.Should().Throw<InvalidOperationException>().WithMessage("*everyday-low-price*parsed 0*");
        }

        [Fact]
        public void NoProductsOnFirstPage_NamesCategoryTitleAndUrl()
        {
            var ex = WoolworthsTileReader.NoProductsOnFirstPage(
                "half-price", "Access Denied", "https://www.woolworths.com.au/shop/browse/specials/half-price?pageNumber=1");

            ex.Message.Should().Contain("half-price").And.Contain("Access Denied").And.Contain("pageNumber=1");
        }

        [Theory]
        [InlineData(false, 0, 0)]
        [InlineData(true, 40, 38)]
        public void EnsureParsed_DoesNotThrowOtherwise(bool sawTiles, int tileCount, int parsedCount)
        {
            var act = () => WoolworthsTileReader.EnsureParsed("half-price", sawTiles, tileCount, parsedCount);

            act.Should().NotThrow();
        }
    }
}
