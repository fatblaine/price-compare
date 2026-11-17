using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using PriceCompareCore.Services.Catalogue;

namespace PriceCompareTests
{
    /// <summary>
    /// BTS-163: parsing of real SaleFinder responses saved on 2026-11-17 (next-week catalogues
    /// for Wed 18 – Tue 24 Nov 2026) in Fixtures/SaleFinder.
    /// </summary>
    public class SaleFinderParserTests
    {
        private static string Fixture(string name) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SaleFinder", name));

        [Fact]
        public void WoolworthsList_ParsesItemsPricesAndListStockcodes()
        {
            var list = SaleFinderParser.ParseItemList(SaleFinderParser.UnwrapJsonp(Fixture("wws_list_68980_first12.jsonp")));

            list.AreaName.Should().Be("NSW Specialty");
            list.SaleName.Should().Be("Weekly Catalogue NSW");
            list.StartDate.Should().Be(new DateOnly(2026, 11, 18));
            list.EndDate.Should().Be(new DateOnly(2026, 11, 24));

            var items = SaleFinderParser.ParseItems(list.ContentHtml, out var listSkus);
            items.Should().HaveCount(12);

            var mango = items[0];
            mango.ItemId.Should().Be("689801001");
            mango.Name.Should().Be("Australian R2E2 Mangoes");
            mango.Price.Should().Be(3.00m);
            mango.PriceUnit.Should().Be("each");
            mango.WasPrice.Should().BeNull();
            mango.PromoText.Should().BeNull();

            var schnitzel = items[1];
            schnitzel.Name.Should().Be("Chicken Sandwich Schnitzels – From the Deli"); // &ndash; decoded
            schnitzel.Price.Should().Be(1.40m);
            schnitzel.SaveAmount.Should().Be(1.40m);
            schnitzel.WasPrice.Should().Be(2.80m);
            schnitzel.PromoText.Should().Be("1/2 Price");

            var coconut = items[2];
            coconut.Price.Should().Be(2.75m);
            coconut.UnitPriceText.Should().Be("$2.75 per litre");
            coconut.ImageUrl.Should().StartWith("https://");

            listSkus["689801001"].Should().Be("143168");
            listSkus["689801003"].Should().Be("724514");
        }

        [Fact]
        public void ColesList_ParsesWasAndSave()
        {
            var list = SaleFinderParser.ParseItemList(SaleFinderParser.UnwrapJsonp(Fixture("coles_list_68878_first28.jsonp")));
            list.AreaName.Should().Be("c-nsw-met");

            var items = SaleFinderParser.ParseItems(list.ContentHtml, out var listSkus);
            items.Should().HaveCount(28);
            listSkus.Should().BeEmpty("Coles lists carry no product codes; they come from the item detail");

            var morningFresh = items.Single(i => i.ItemId == "688781001");
            morningFresh.Name.Should().Be("Morning Fresh Dishwashing Liquid 900mL");
            morningFresh.Price.Should().Be(4.75m);
            morningFresh.WasPrice.Should().Be(10.00m);
            morningFresh.SaveAmount.Should().Be(5.25m);
            morningFresh.PromoText.Should().BeNull();
            morningFresh.UnitPriceText.Should().Be("$0.53 per 100mL");
        }

        [Theory]
        [InlineData("wws_tooltip_689801003.jsonp", new[] { "724514", "500999", "6073818", "886724", "6024207", "6074120" })]
        [InlineData("coles_tooltip_688781001.jsonp", new[] { "3208890", "5091513", "6318192", "9735935", "9736075" })]
        public void Tooltip_ListsEveryProductCodeOfTheItem(string fixture, string[] expected)
        {
            var html = SaleFinderParser.ParseTooltipHtml(SaleFinderParser.UnwrapJsonp(Fixture(fixture)));

            SaleFinderParser.ParseSkuList(html).Should().Equal(expected);
        }

        [Fact]
        public void Sales_PicksNextWeeklyCatalogueForWoolworths()
        {
            var sales = SaleFinderParser.ParseSales(Fixture("wws_sales_store4777.json"));

            sales.Should().HaveCount(3);
            var next = SaleFinderParser.PickNextSale(sales, new DateOnly(2026, 11, 17), "Weekly Catalogue");
            next!.SaleId.Should().Be(68980);
            next.StartDate.Should().Be(new DateOnly(2026, 11, 18));
            next.PublishDate.Should().Be(new DateTime(2026, 11, 16, 17, 0, 0));

            // On the start day it is this week's catalogue, not next week's.
            SaleFinderParser.PickNextSale(sales, new DateOnly(2026, 11, 18), "Weekly Catalogue").Should().BeNull();
        }

        [Fact]
        public void Sales_SkipsLiquorlandAndEventCataloguesForColes()
        {
            var sales = SaleFinderParser.ParseSales(Fixture("coles_sales_store8442.json"));

            sales.Select(s => s.SaleName).Should().Contain(n => n.Contains("Liquorland"));
            SaleFinderParser.PickNextSale(sales, new DateOnly(2026, 11, 17), "Coles Catalogue")!.SaleId.Should().Be(68878);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void UnwrapJsonp_EmptyBodyIsAnError(string? body)
        {
            // SaleFinder answers 200 + empty body for a non-jQuery callback name; that must not read as "0 items".
            var act = () => SaleFinderParser.UnwrapJsonp(body);

            act.Should().Throw<InvalidOperationException>().WithMessage("*empty*");
        }

        [Fact]
        public void MultiBuy_StoresPerUnitPriceAndMarksIt()
        {
            const string html = @"<div id=""sf-items-table"">
<a class=""sf-item"" href="""" data-itemid=""1"">
  <h4 class=""sf-item-heading"">Pepsi Max 1.25L</h4>
  <div class=""sf-price-options"">
    <span class=""sf-nowprice""><span class=""sf-saleoptiondesc"">Any 2 for</span><span class=""sf-pricedisplay"">$15.00</span><span class=""sf-optionsuffix""></span></span>
    <span class=""sf-regoption""><span class=""sf-regoptiondesc"">Save from</span><span class=""sf-regprice"">$1.00</span></span>
  </div>
</a></div>";

            var item = SaleFinderParser.ParseItems(html, out _).Single();

            item.Price.Should().Be(7.50m);
            item.PriceUnit.Should().Be("multibuy");
            item.WasPrice.Should().BeNull();
            item.PromoText.Should().Be("Any 2 for $15.00, Save from $1.00");
        }

        [Fact]
        public void ItemWithoutPrice_IsSkipped()
        {
            const string html = @"<a class=""sf-item"" data-itemid=""9""><h4 class=""sf-item-heading"">15% off gift cards</h4>
<div class=""sf-price-options""><span class=""sf-nowprice""></span></div></a>";

            SaleFinderParser.ParseItems(html, out _).Should().BeEmpty();
        }

        [Theory]
        [InlineData("Range was", "4.20", 2.30, 4.20, 1.90, null)]
        [InlineData("Price Drop, Was on 20/10", "24.00", 20.00, 24.00, 4.00, "Price Drop")]
        [InlineData("Every Day", "", 3.50, null, null, "Every Day")]
        [InlineData("40% off, Save", "2.00", 3.00, 5.00, 2.00, "40% off")]
        public void RegularPriceVariants(string desc, string regPrice, double price, double? was, double? save, string? promo)
        {
            var html = $@"<a class=""sf-item"" data-itemid=""5""><h4 class=""sf-item-heading"">Thing</h4>
<div class=""sf-price-options"">
<span class=""sf-nowprice""><span class=""sf-pricedisplay"">${price:0.00}</span><span class=""sf-optionsuffix"">each</span></span>
<span class=""sf-regoption""><span class=""sf-regoptiondesc"">{desc}</span><span class=""sf-regprice"">{(regPrice.Length > 0 ? "$" + regPrice : "")}</span></span>
</div></a>";

            var item = SaleFinderParser.ParseItems(html, out _).Single();

            item.Price.Should().Be((decimal)price);
            item.WasPrice.Should().Be(was is null ? null : (decimal)was.Value);
            item.SaveAmount.Should().Be(save is null ? null : (decimal)save.Value);
            item.PromoText.Should().Be(promo);
        }
    }
}
