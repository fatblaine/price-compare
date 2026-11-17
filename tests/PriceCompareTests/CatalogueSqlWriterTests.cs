using System;
using FluentAssertions;
using PriceCompareCore.Services.Catalogue;

namespace PriceCompareTests
{
    /// <summary>BTS-163: the generated script stores a catalogue and matches its SKUs in the target DB.</summary>
    public class CatalogueSqlWriterTests
    {
        private static readonly DateTime ScrapedAt = new(2026, 11, 17, 7, 30, 0, DateTimeKind.Utc);

        private static CatalogueItemWithSkus Item(string id, string name, decimal price, params string[] skus) =>
            new(new ParsedCatalogueItem(id, name, price, "each", null, null, null, null, null), skus);

        [Fact]
        public void Woolworths_MatchesOnStockcodeInsideOneTransaction()
        {
            var sql = CatalogueSqlWriter.Build(1, 68980, "NSW Specialty", new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 24), ScrapedAt,
                new[] { Item("689801003", "Cocobella Coconut Water 1 Litre", 2.75m, "724514", "500999") });

            sql.TrimStart().Should().Contain("BEGIN;");
            sql.TrimEnd().Should().EndWith("COMMIT;");
            sql.Should().Contain("ON CONFLICT (shoptype, saleid, itemid) DO UPDATE SET");
            sql.Should().Contain("(1, 68980, '689801003', '724514')");
            sql.Should().Contain("(1, 68980, '689801003', '500999')");
            sql.Should().Contain("k.sku = p.stockcode");
            sql.Should().NotContain("k.sku = p.sourceid");
            sql.Should().Contain("DATE '2026-11-18'");
            sql.Should().Contain("TIMESTAMPTZ '2026-11-17 07:30:00.000000+00'");
        }

        [Fact]
        public void Coles_MatchesOnSourceId()
        {
            var sql = CatalogueSqlWriter.Build(0, 68933, "c-vic-met", new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 24), ScrapedAt,
                new[] { Item("689331001", "Morning Fresh Dishwashing Liquid 900mL", 4.75m, "9736075") });

            sql.Should().Contain("k.sku = p.sourceid");
            sql.Should().NotContain("k.sku = p.stockcode");
        }

        [Fact]
        public void QuotesInTextAreEscaped()
        {
            var sql = CatalogueSqlWriter.Build(1, 1, "NSW", new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 24), ScrapedAt,
                new[] { Item("1", "Arnott's Tim Tam {Original}", 3m, "1") });

            sql.Should().Contain("'Arnott''s Tim Tam {Original}'");
        }

        [Fact]
        public void RebuildsSkusAndRemovesDroppedItemsForTheSale()
        {
            var sql = CatalogueSqlWriter.Build(1, 68980, "NSW", new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 24), ScrapedAt,
                new[] { Item("a", "A", 1m, "11", "11"), Item("b", "B", 2m) });

            sql.Should().Contain("DELETE FROM catalogue_special WHERE shoptype = 1 AND saleid = 68980 AND itemid NOT IN ('a', 'b');");
            sql.Should().Contain("DELETE FROM catalogue_special_sku WHERE shoptype = 1 AND saleid = 68980;");
            CountOf(sql, "(1, 68980, 'a', '11')").Should().Be(1, "duplicate SKUs are written once");
        }

        [Fact]
        public void KeepsEightWeeksOfCatalogues()
        {
            var sql = CatalogueSqlWriter.Build(1, 1, "NSW", new DateOnly(2026, 11, 18), new DateOnly(2026, 11, 24), ScrapedAt,
                new[] { Item("a", "A", 1m, "1") });

            sql.Should().Contain("DELETE FROM catalogue_special WHERE validto < DATE '2026-09-29';");
        }

        private static int CountOf(string text, string value)
        {
            var count = 0;
            for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }
    }
}
