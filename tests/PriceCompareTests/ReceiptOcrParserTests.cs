using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using PriceCompareCore.Services;
using PriceCompareData.Entities.Receipts;
using Xunit;

namespace PriceCompareTests
{
    public class ReceiptOcrParserTests
    {
        private static List<OcrTextLine> Lines(params string[] texts) =>
            texts.Select(t => new OcrTextLine { Text = t, Confidence = 0.99f }).ToList();

        [Fact]
        public void DetectStoreName_ReturnsKnownBrands()
        {
            var parser = new ReceiptOcrParser();
            var lines = Lines("Welcome to COLES Sydney", "other");

            parser.TryDetectStoreName(lines).Should().Be("Coles");
        }

        [Fact]
        public void DetectStoreName_FallsBackToUppercaseHeader()
        {
            var parser = new ReceiptOcrParser();
            var lines = Lines("some header", "MYSHOP", "more");

            parser.TryDetectStoreName(lines).Should().Be("MYSHOP");
        }

        [Fact]
        public void DetectPurchaseDate_ParsesDateTimeToken()
        {
            var parser = new ReceiptOcrParser();
            var lines = Lines("Date 24/10/2025 18:37", "other");

            parser.TryDetectPurchaseDate(lines).Should().Be(new DateTime(2025, 10, 24, 18, 37, 0));
        }

        [Fact]
        public void ExtractItems_ParsesColesStyleLines()
        {
            var parser = new ReceiptOcrParser();
            var lines = new List<OcrTextLine>
            {
                new OcrTextLine { Text = "Description", Confidence = 0.95f },
                new OcrTextLine { Text = "* BLUEBERRIES 170GRAM    3.50", Confidence = 0.95f },
                new OcrTextLine { Text = "MILK 3L  4.65", Confidence = 0.95f },
                new OcrTextLine { Text = "TOTAL 8.15", Confidence = 0.95f }
            };

            var items = parser.ExtractItems(lines);

            items.Should().HaveCount(2);
            items[0].ProductName.Should().Be("BLUEBERRIES 170GRAM");
            items[0].Price.Should().Be(3.50m);
            items[1].ProductName.Should().Be("MILK 3L");
            items[1].Price.Should().Be(4.65m);
        }

        [Fact]
        public void ExtractItems_ParsesWoolworthsQtyLines()
        {
            var parser = new ReceiptOcrParser();
            var lines = new List<OcrTextLine>
            {
                new OcrTextLine { Text = "Description", Confidence = 0.95f },
                new OcrTextLine { Text = "Banana Loose", Confidence = 0.95f },
                new OcrTextLine { Text = "Qty 2 @ $1.50 each        3.00", Confidence = 0.95f },
                new OcrTextLine { Text = "Total 3.00", Confidence = 0.95f }
            };

            var items = parser.ExtractItems(lines);

            items.Should().HaveCount(1);
            items[0].ProductName.Should().Be("Banana Loose");
            items[0].Quantity.Should().Be(2);
            items[0].Price.Should().Be(1.50m);
        }
    }
}
