using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PriceCompareCore.Services.Catalogue
{
    public interface ISaleFinderClient
    {
        Task<IReadOnlyList<SaleFinderSale>> GetSalesAsync(int shopType, CatalogueRetailerOptions retailer, CancellationToken ct);
        Task<SaleFinderItemList> GetItemListAsync(int saleId, CatalogueRetailerOptions retailer, CancellationToken ct);
        Task<IReadOnlyList<string>> GetItemSkusAsync(string itemId, CatalogueRetailerOptions retailer, CancellationToken ct);
    }

    /// <summary>
    /// Plain HTTP access to SaleFinder, which hosts both supermarkets' catalogues (BTS-163 P0:
    /// no browser, cookie or token needed). Endpoints and quirks are documented in
    /// docs/scraping/next-week-catalogue-plan.md §0.5 / §4.6.
    /// </summary>
    public sealed class SaleFinderClient : ISaleFinderClient
    {
        public const string HttpClientName = "SaleFinder";

        private const string WebServiceBase = "https://webservice.salefinder.com.au/index.php/api";
        private const string EmbedBase = "https://embed.salefinder.com.au";
        private const string WoolworthsCataloguePage = "https://www.woolworths.com.au/shop/catalogue";

        private static readonly Regex WoolworthsKeyRegex = new(@"apikey=([A-Za-z0-9]+)", RegexOptions.Compiled);
        private static readonly Regex EmbedKeyRegex = new(@"apiKey:'([^']+)'", RegexOptions.Compiled);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Dictionary<int, string> _apiKeys = new();

        public SaleFinderClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<IReadOnlyList<SaleFinderSale>> GetSalesAsync(int shopType, CatalogueRetailerOptions retailer, CancellationToken ct)
        {
            var key = await GetApiKeyAsync(shopType, retailer, ct);
            var url = $"{WebServiceBase}/sales/retailer/?id={retailer.RetailerId}&apikey={Uri.EscapeDataString(key)}"
                      + $"&format=json&storeId={Uri.EscapeDataString(retailer.StoreId)}";
            var body = await GetStringAsync(url, ct);
            if (string.IsNullOrWhiteSpace(body))
            {
                throw new InvalidOperationException($"SaleFinder sales list for retailer {retailer.RetailerId} was empty.");
            }

            return SaleFinderParser.ParseSales(body);
        }

        public async Task<SaleFinderItemList> GetItemListAsync(int saleId, CatalogueRetailerOptions retailer, CancellationToken ct)
        {
            // rows_per_page=1000 returns the whole catalogue (≈300–400 items) in one response.
            var url = $"{EmbedBase}/productlist/view/{saleId}/?locationId={Uri.EscapeDataString(retailer.StoreId)}"
                      + SaleGroupQuery(retailer)
                      + "&preview=&rows_per_page=1000&callback=" + NewCallbackName();
            var body = await GetStringAsync(url, ct);
            return SaleFinderParser.ParseItemList(SaleFinderParser.UnwrapJsonp(body));
        }

        public async Task<IReadOnlyList<string>> GetItemSkusAsync(string itemId, CatalogueRetailerOptions retailer, CancellationToken ct)
        {
            var url = $"{EmbedBase}/item/tooltip/{Uri.EscapeDataString(itemId)}?callback={NewCallbackName()}&preview="
                      + SaleGroupQuery(retailer)
                      + $"&locationId={Uri.EscapeDataString(retailer.StoreId)}";
            var body = await GetStringAsync(url, ct);
            var html = SaleFinderParser.ParseTooltipHtml(SaleFinderParser.UnwrapJsonp(body));
            return SaleFinderParser.ParseSkuList(html);
        }

        /// <summary>
        /// SaleFinder only answers JSONP when the callback looks like jQuery's
        /// ("jQuery1720…_&lt;unix ms&gt;"); "callback=cb" gets HTTP 200 with an empty body.
        /// </summary>
        public static string NewCallbackName()
        {
            var digits = Random.Shared.NextInt64(1_000_000_000_000, 9_999_999_999_999).ToString(CultureInfo.InvariantCulture);
            return $"jQuery1720{digits}_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture)}";
        }

        private static string SaleGroupQuery(CatalogueRetailerOptions retailer)
        {
            var query = string.IsNullOrWhiteSpace(retailer.SaleGroup) ? string.Empty : $"&saleGroup={Uri.EscapeDataString(retailer.SaleGroup)}";
            if (!string.IsNullOrWhiteSpace(retailer.IncludeSaleGroup))
            {
                query += $"&includeSaleGroup={Uri.EscapeDataString(retailer.IncludeSaleGroup)}";
            }

            return query;
        }

        /// <summary>
        /// The public front-end key, read at run time so a rotation does not break the job:
        /// Woolworths embeds it in the catalogue page, Coles in SaleFinder's embed script.
        /// </summary>
        private async Task<string> GetApiKeyAsync(int shopType, CatalogueRetailerOptions retailer, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(retailer.ApiKey))
            {
                return retailer.ApiKey!;
            }

            if (_apiKeys.TryGetValue(retailer.RetailerId, out var cached))
            {
                return cached;
            }

            string? key;
            if (shopType == 1)
            {
                var page = await GetStringAsync(WoolworthsCataloguePage, ct);
                key = WoolworthsKeyRegex.Match(page).Groups[1].Value;
            }
            else
            {
                var script = await GetStringAsync($"{EmbedBase}/{retailer.RetailerId}/", ct);
                key = EmbedKeyRegex.Match(script).Groups[1].Value;
            }

            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException($"Could not read the SaleFinder API key for retailer {retailer.RetailerId}.");
            }

            _apiKeys[retailer.RetailerId] = key;
            return key;
        }

        private async Task<string> GetStringAsync(string url, CancellationToken ct)
        {
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct);
        }
    }
}
