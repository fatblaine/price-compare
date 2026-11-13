using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;
using PriceCompareData.Entities.Scraping;

namespace PriceCompareCore.Services
{
    /// <summary>Name and prices read from one Woolworths product tile.</summary>
    public sealed record WoolworthsTileData(string Name, decimal Price, decimal? WasPrice, string? PromoText);

    /// <summary>
    /// Reads product data straight from a Woolworths product tile's DOM (name, lead price, was price,
    /// multi-buy text). Shared by the WWS DOM scrapers since Woolworths now spells prices out in words
    /// in the link aria-label ("Price twenty dollars"), which the old aria parsing cannot read.
    /// </summary>
    public static class WoolworthsTileReader
    {
        // Climbs from a product link to its tile: the highest ancestor that still holds exactly one
        // lead price. Returns a JSON string so the result does not depend on Playwright's deserializer.
        private const string TileScript = @"a => {
            const sel = ""[class*='lead-price-value']"";
            let t = a;
            for (let i = 0; i < 15 && t && !(t.querySelector && t.querySelector(sel)); i++) t = t.parentElement;
            if (!t || !t.querySelector || t.querySelectorAll(sel).length !== 1) return null;
            while (t.parentElement && t.parentElement.querySelectorAll(sel).length === 1) t = t.parentElement;
            const text = e => e ? e.textContent.replace(/\s+/g, ' ').trim() : null;
            const all = (t.innerText || t.textContent || '').replace(/\s+/g, ' ');
            let was = text(t.querySelector(""[class*='was-amount']""));
            if (!was) { const m = all.match(/Was\s*\$\s*\d+(?:\.\d+)?/i); was = m ? m[0] : null; }
            const promo = all.match(/\b\d+\s+for\s+\$\s*\d+(?:\.\d+)?/i);
            return JSON.stringify({
                name: text(t.querySelector(""[class*='display-name']"")),
                price: text(t.querySelector(sel)),
                was: was,
                promo: promo ? promo[0] : null
            });
        }";

        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        /// <summary>Reads the tile around a product link; null when the tile has no readable name or price.</summary>
        public static async Task<WoolworthsTileData?> ReadAsync(ILocator anchor)
        {
            try
            {
                var json = await anchor.EvaluateAsync<string?>(TileScript);
                if (string.IsNullOrWhiteSpace(json))
                {
                    return null;
                }

                var raw = JsonSerializer.Deserialize<RawTile>(json, JsonOptions);
                return raw is null ? null : FromRaw(raw.Name, raw.Price, raw.Was, raw.Promo);
            }
            catch (PlaywrightException)
            {
                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Validates and parses raw tile text; null when the name or price is missing.</summary>
        public static WoolworthsTileData? FromRaw(string? name, string? price, string? was, string? promo)
        {
            var cleanName = name?.Trim() ?? string.Empty;
            if (cleanName.Length < 3 || !cleanName.Any(char.IsLetter))
            {
                return null;
            }

            var parsedPrice = ParseAmount(price);
            if (!parsedPrice.HasValue || parsedPrice.Value <= 0)
            {
                return null;
            }

            var parsedWas = ParseAmount(was);
            if (parsedWas.HasValue && parsedWas.Value <= parsedPrice.Value)
            {
                parsedWas = null;
            }

            var cleanPromo = string.IsNullOrWhiteSpace(promo) ? null : promo.Trim();
            return new WoolworthsTileData(cleanName, parsedPrice.Value, parsedWas, cleanPromo);
        }

        /// <summary>
        /// Parses the first dollar amount in tile text: "20.00", "$8.00", "Was $7 - 10/11/2026" (date ignored).
        /// </summary>
        public static decimal? ParseAmount(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var match = Regex.Match(raw.Replace(",", string.Empty), @"\$?\s*(\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return null;
            }

            return decimal.TryParse(match.Groups[1].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : null;
        }

        /// <summary>Builds the scraped product from tile data; savings is was minus price when a was price exists.</summary>
        public static WoolworthsSpecialProduct ToProduct(WoolworthsTileData tile, string? image, DateTime scrapedAt, string? promoText)
        {
            return new WoolworthsSpecialProduct
            {
                Stockcode = 0,
                Barcode = string.Empty,
                DisplayName = tile.Name,
                Brand = string.Empty,
                Price = tile.Price,
                WasPrice = tile.WasPrice,
                SavingsAmount = tile.WasPrice.HasValue ? tile.WasPrice.Value - tile.Price : null,
                IsOnSpecial = true,
                CupPrice = null,
                CupString = null,
                LargeImageFile = image ?? string.Empty,
                PromoText = promoText,
                ScrapedAt = scrapedAt
            };
        }

        /// <summary>
        /// Fails the scrape when the page showed product tiles but nothing could be parsed, so a page-format
        /// change surfaces as a failed job instead of a silent "succeeded with 0 products".
        /// </summary>
        public static void EnsureParsed(string category, bool sawProductTiles, int tileCount, int parsedCount)
        {
            if (sawProductTiles && parsedCount == 0)
            {
                throw new InvalidOperationException(
                    $"WWS DOM {category}: found product tiles ({tileCount} collected) but parsed 0 products; the page format has probably changed.");
            }
        }

        /// <summary>
        /// Fails the scrape when the first page never showed any product: usually an Akamai block page or a
        /// changed URL. The page title and URL go into the message so the job run says which one it was.
        /// </summary>
        public static InvalidOperationException NoProductsOnFirstPage(string category, string? pageTitle, string? pageUrl)
        {
            return new InvalidOperationException(
                $"WWS DOM {category}: no products on the first page (title '{pageTitle}', url {pageUrl}); probably blocked or the page changed.");
        }

        private sealed class RawTile
        {
            public string? Name { get; set; }
            public string? Price { get; set; }
            public string? Was { get; set; }
            public string? Promo { get; set; }
        }
    }
}
