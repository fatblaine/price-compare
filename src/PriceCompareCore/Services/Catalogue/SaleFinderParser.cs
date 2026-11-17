using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace PriceCompareCore.Services.Catalogue
{
    /// <summary>
    /// Pure parsing of SaleFinder responses (BTS-163). Both retailers render items with the same
    /// sf-* classes (checked on the 2026-11-18 catalogues, fixtures in tests/Fixtures/SaleFinder).
    /// </summary>
    public static class SaleFinderParser
    {
        private static readonly Regex JsonpRegex =
            new(@"^\s*[\w$.]+\((.*)\)\s*;?\s*$", RegexOptions.Singleline | RegexOptions.Compiled);
        private static readonly Regex AmountRegex =
            new(@"(\d+(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex MultiBuyRegex =
            new(@"(\d+)\s+for\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex WasSaveRegex =
            new(@"^Was\s*\$\s*(\d+(?:\.\d+)?)\s*,\s*Save$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SkuListRegex =
            new(@"data-skulist=""([^""]*)""", RegexOptions.Compiled);
        private static readonly Regex RangeWords =
            new(@"\b(varieties|selected|range)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Returns the JSON inside a JSONP wrapper. SaleFinder answers 200 with an empty body when the
        /// callback name is not jQuery-style, so an empty or unwrapped body is an error, not "no items".
        /// </summary>
        public static string UnwrapJsonp(string? body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                throw new InvalidOperationException("SaleFinder returned an empty response (check the JSONP callback name).");
            }

            var match = JsonpRegex.Match(body);
            if (!match.Success)
            {
                throw new InvalidOperationException("SaleFinder response is not JSONP.");
            }

            return match.Groups[1].Value;
        }

        /// <summary>Parses webservice …/api/sales/retailer/ (format=json).</summary>
        public static IReadOnlyList<SaleFinderSale> ParseSales(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var result = new List<SaleFinderSale>();
            if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var wrapper in items.EnumerateArray())
            {
                // Each element is wrapped once more: { "items": { "saleId": … } }
                var sale = wrapper.TryGetProperty("items", out var inner) ? inner : wrapper;
                var saleId = ReadInt(sale, "saleId");
                var start = ReadDate(sale, "startDate");
                var end = ReadDate(sale, "endDate");
                if (saleId is null || start is null || end is null)
                {
                    continue;
                }

                var publish = ReadDateTime(sale, "publishDate") ?? start.Value.ToDateTime(TimeOnly.MinValue);
                result.Add(new SaleFinderSale(saleId.Value, ReadString(sale, "saleName") ?? string.Empty, publish, start.Value, end.Value));
            }

            return result;
        }

        /// <summary>
        /// The upcoming weekly catalogue: starts after today and its name contains <paramref name="nameContains"/>.
        /// </summary>
        public static SaleFinderSale? PickNextSale(IEnumerable<SaleFinderSale> sales, DateOnly today, string nameContains)
        {
            return sales
                .Where(s => s.StartDate > today)
                .Where(s => string.IsNullOrWhiteSpace(nameContains)
                            || s.SaleName.Contains(nameContains, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.StartDate)
                .ThenBy(s => s.SaleId)
                .FirstOrDefault();
        }

        /// <summary>Parses the JSON inside the productlist/view JSONP.</summary>
        public static SaleFinderItemList ParseItemList(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var start = ReadDate(root, "startDate") ?? throw new InvalidOperationException("SaleFinder item list has no startDate.");
            var end = ReadDate(root, "endDate") ?? throw new InvalidOperationException("SaleFinder item list has no endDate.");
            return new SaleFinderItemList(
                ReadString(root, "areaName") ?? string.Empty,
                ReadString(root, "saleName") ?? string.Empty,
                start,
                end,
                ReadString(root, "content") ?? string.Empty);
        }

        /// <summary>Parses all priced items from the item-list HTML. Items without a price (e.g. gift-card offers) are skipped.</summary>
        public static IReadOnlyList<ParsedCatalogueItem> ParseItems(string html, out IReadOnlyDictionary<string, string> listSkus)
        {
            var parser = new HtmlParser();
            using var document = parser.ParseDocument(html ?? string.Empty);
            var items = new List<ParsedCatalogueItem>();
            var skus = new Dictionary<string, string>();
            var seen = new HashSet<string>();

            foreach (var heading in document.QuerySelectorAll(".sf-item-heading"))
            {
                var root = FindItemRoot(heading);
                if (root is null)
                {
                    continue;
                }

                var itemId = heading.GetAttribute("data-itemid")
                             ?? root.GetAttribute("data-itemid")
                             ?? root.QuerySelector("[data-itemid]")?.GetAttribute("data-itemid");
                if (string.IsNullOrWhiteSpace(itemId) || !seen.Add(itemId))
                {
                    continue;
                }

                var parsed = ParseItem(root, itemId, Clean(heading.TextContent));
                if (parsed is null)
                {
                    continue;
                }

                items.Add(parsed);

                // Woolworths shows the first stockcode on the list; used when the detail call has none.
                var stockcode = (root.HasAttribute("data-stockcode") ? root : root.QuerySelector("[data-stockcode]"))
                    ?.GetAttribute("data-stockcode");
                if (!string.IsNullOrWhiteSpace(stockcode))
                {
                    skus[itemId] = stockcode.Trim();
                }
            }

            listSkus = skus;
            return items;
        }

        /// <summary>All product codes an item covers, from the item/tooltip HTML (data-skulist).</summary>
        public static IReadOnlyList<string> ParseSkuList(string tooltipHtml)
        {
            var match = SkuListRegex.Match(tooltipHtml ?? string.Empty);
            if (!match.Success)
            {
                return Array.Empty<string>();
            }

            return match.Groups[1].Value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>The item/tooltip JSONP body is a JSON string holding HTML.</summary>
        public static string ParseTooltipHtml(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind switch
            {
                JsonValueKind.String => doc.RootElement.GetString() ?? string.Empty,
                JsonValueKind.Object when doc.RootElement.TryGetProperty("content", out var c) => c.GetString() ?? string.Empty,
                _ => string.Empty,
            };
        }

        public static bool LooksLikeRange(string name) => RangeWords.IsMatch(name ?? string.Empty);

        private static IElement? FindItemRoot(IElement heading)
        {
            var root = heading.Closest("a.sf-item, .shelfProductStamp");
            if (root is not null)
            {
                return root;
            }

            for (var e = heading.ParentElement; e is not null; e = e.ParentElement)
            {
                if (e.QuerySelector(".sf-price-options") is not null)
                {
                    return e;
                }
            }

            return null;
        }

        private static ParsedCatalogueItem? ParseItem(IElement root, string itemId, string name)
        {
            var nowPrice = root.QuerySelector(".sf-nowprice");
            var display = ParseAmount(nowPrice?.QuerySelector(".sf-pricedisplay")?.TextContent);
            if (display is null || display <= 0 || string.IsNullOrWhiteSpace(name))
            {
                return null;
            }

            var promo = new List<string>();
            var price = display.Value;
            var unit = NullIfEmpty(Clean(nowPrice?.QuerySelector(".sf-optionsuffix")?.TextContent));
            var multiBuy = false;

            // "Any 2 for $15.00": the displayed price is for the bundle; store the per-unit price and
            // mark it so it is never compared with a single-item shelf price.
            var saleDesc = Clean(root.QuerySelector(".sf-saleoptiondesc")?.TextContent);
            var multi = MultiBuyRegex.Match(saleDesc);
            if (multi.Success && int.TryParse(multi.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var qty) && qty > 1)
            {
                multiBuy = true;
                promo.Add($"{saleDesc} ${display.Value.ToString("0.00", CultureInfo.InvariantCulture)}");
                price = Math.Round(display.Value / qty, 2, MidpointRounding.AwayFromZero);
                unit = "multibuy";
            }

            decimal? was = null;
            decimal? save = null;
            var regDesc = Clean(root.QuerySelector(".sf-regoptiondesc")?.TextContent);
            var regAmount = ParseAmount(root.QuerySelector(".sf-regprice")?.TextContent);

            var wasSave = WasSaveRegex.Match(regDesc);
            if (wasSave.Success)
            {
                // Coles: "Was $10.00, Save" + "$5.25"
                was = ParseAmount(wasSave.Groups[1].Value);
                save = regAmount;
            }
            else if (regDesc.Equals("Save from", StringComparison.OrdinalIgnoreCase))
            {
                if (regAmount is not null)
                {
                    promo.Add($"Save from ${regAmount.Value.ToString("0.00", CultureInfo.InvariantCulture)}");
                }
            }
            else if (regDesc.EndsWith("Save", StringComparison.OrdinalIgnoreCase))
            {
                // "1/2 Price, Save" / "40% off, Save" / "Save" + amount saved
                var label = regDesc[..^"Save".Length].TrimEnd(' ', ',');
                if (label.Length > 0)
                {
                    promo.Add(label);
                }

                save = regAmount;
                if (save is not null && !multiBuy)
                {
                    was = price + save.Value;
                }
            }
            else if (regDesc.StartsWith("Was", StringComparison.OrdinalIgnoreCase)
                     || regDesc.StartsWith("Range was", StringComparison.OrdinalIgnoreCase)
                     || regDesc.StartsWith("Price Drop", StringComparison.OrdinalIgnoreCase))
            {
                if (regDesc.StartsWith("Price Drop", StringComparison.OrdinalIgnoreCase))
                {
                    promo.Add("Price Drop");
                }

                was = regAmount;
                if (was is not null && was > price && !multiBuy)
                {
                    save = was.Value - price;
                }
            }
            else if (regDesc.Length > 0)
            {
                // "Every Day", "Everyday Low Price", "Introductory Offer", …
                promo.Add(regDesc);
            }

            if (was is not null && was <= price)
            {
                was = null;
            }

            return new ParsedCatalogueItem(
                itemId.Trim(),
                name,
                price,
                unit,
                was,
                save,
                promo.Count == 0 ? null : string.Join(", ", promo),
                NullIfEmpty(Clean(root.QuerySelector(".sf-comparativeText")?.TextContent)),
                NullIfEmpty(root.QuerySelector("img")?.GetAttribute("src")));
        }

        private static decimal? ParseAmount(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            var match = AmountRegex.Match(text.Replace(",", string.Empty));
            return match.Success && decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var v)
                ? v
                : null;
        }

        private static string Clean(string? text) =>
            Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();

        private static string? NullIfEmpty(string? text) =>
            string.IsNullOrWhiteSpace(text) ? null : text.Trim();

        private static string? ReadString(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static int? ReadInt(JsonElement e, string name)
        {
            if (!e.TryGetProperty(name, out var v))
            {
                return null;
            }

            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n))
            {
                return n;
            }

            return v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var s)
                ? s
                : null;
        }

        private static DateTime? ReadDateTime(JsonElement e, string name)
        {
            var text = ReadString(e, name);
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt : null;
        }

        private static DateOnly? ReadDate(JsonElement e, string name)
        {
            var dt = ReadDateTime(e, name);
            return dt is null ? null : DateOnly.FromDateTime(dt.Value);
        }
    }
}
