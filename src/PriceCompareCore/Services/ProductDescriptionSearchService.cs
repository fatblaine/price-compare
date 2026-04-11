using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
using PriceCompareCore.Exceptions;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Services;

public class ProductDescriptionSearchService : IProductDescriptionSearchService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDistributedCache _cache;
    private readonly AppDbContext _db;
    private readonly OpenRouterOptions _options;
    private readonly ILogger<ProductDescriptionSearchService> _logger;

    private const int DailyLimit = 3;
    private const int LlmTimeoutSeconds = 10;
    private const string DescriptionSearchModel = "openai/gpt-4o-mini";

    public ProductDescriptionSearchService(
        IHttpClientFactory httpClientFactory,
        IDistributedCache cache,
        AppDbContext db,
        IOptions<OpenRouterOptions> options,
        ILogger<ProductDescriptionSearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _cache = cache;
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<DescriptionSearchResponse> SearchAsync(DescriptionSearchRequest request, string callerId)
    {
        var usageKey = $"ai_search:{callerId}";
        var storedBytes = await _cache.GetAsync(usageKey);
        var currentUsage = storedBytes != null ? BitConverter.ToInt32(storedBytes) : 0;

        if (currentUsage >= DailyLimit)
        {
            throw new AiSearchRateLimitExceededException();
        }

        List<string> keywords;
        bool llmSucceeded = false;

        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            var inferredProducts = await InferProductNamesAsync(request.Query);
            if (inferredProducts != null && inferredProducts.Count > 0)
            {
                keywords = inferredProducts;
                llmSucceeded = true;
                _logger.LogInformation(
                    "LLM inferred products for query '{Query}': [{Terms}]",
                    request.Query, string.Join(", ", keywords));
            }
            else
            {
                _logger.LogWarning(
                    "LLM returned no results for query '{Query}'. Fallback disabled — returning empty.", request.Query);
                keywords = new List<string>();
            }
        }
        else
        {
            _logger.LogWarning(
                "OpenRouter API key not configured. AI search unavailable — returning empty.");
            keywords = new List<string>();
        }

        if (llmSucceeded)
        {
            var newUsage = currentUsage + 1;
            await _cache.SetAsync(
                usageKey,
                BitConverter.GetBytes(newUsage),
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
                });
            currentUsage = newUsage;
        }

        var topN = Math.Clamp(request.TopN, 1, 20);
        var products = await SearchProductsAsync(keywords, topN, request.ShopType);

        return new DescriptionSearchResponse
        {
            Query = request.Query,
            InferredProducts = llmSucceeded ? keywords : new List<string>(),
            RemainingSearches = Math.Max(0, DailyLimit - currentUsage),
            Products = products
        };
    }

    private async Task<List<string>?> InferProductNamesAsync(string userQuery)
    {
        const string systemPrompt = """
            You are a shopping assistant for an Australian supermarket (Coles/Woolworths).
            Given a user's description in any language, infer what product(s) they are looking for.
            Think semantically: "昆虫制作的糖浆" → "honey"; "洗头发的" → "shampoo".
            Output 1 to 5 short English product name fragments that would match actual supermarket product names.
            IMPORTANT: Always use the singular base form of each product name (e.g. "tim tam" not "tim tams", "biscuit" not "biscuits", "chip" not "chips").
            Return ONLY a JSON array of lowercase strings. No explanation. No markdown. No code blocks.
            Example output: ["honey"] or ["shampoo", "conditioner"]
            """;

        var payload = new
        {
            model = DescriptionSearchModel,
            temperature = 0.2,
            max_tokens = 100,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user",   content = userQuery }
            }
        };

        try
        {
            var client = _httpClientFactory.CreateClient("OpenRouter");

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "chat/completions");
            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            httpRequest.Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LlmTimeoutSeconds));
            var response = await client.SendAsync(httpRequest, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "OpenRouter returned {StatusCode} for keyword extraction.",
                    (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            using var doc = JsonDocument.Parse(json);

            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                ?.Trim();

            if (string.IsNullOrWhiteSpace(content))
                return null;

            try
            {
                var keywords = JsonSerializer.Deserialize<List<string>>(content);
                return keywords?
                    .Where(k => !string.IsNullOrWhiteSpace(k))
                    .Select(k => k.ToLowerInvariant().Trim())
                    .Take(5)
                    .ToList();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not parse LLM response as JSON array. Raw content: {Content}",
                    content);
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "OpenRouter keyword extraction timed out after {Seconds}s for query '{Query}'.",
                LlmTimeoutSeconds, userQuery);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during OpenRouter keyword extraction.");
            return null;
        }
    }

    private async Task<List<ProductSearchItem>> SearchProductsAsync(List<string> keywords, int topN, int? shopTypeFilter)
    {
        // Build a subquery for latest scrape date per shop — same Join pattern as ProductService.cs
        var latestPerShop = _db.Products
            .AsNoTracking()
            .Where(p => p.ShopType.HasValue)
            .GroupBy(p => p.ShopType)
            .Select(g => new { ShopType = g.Key, MaxDate = g.Max(p => p.LastSeenAt.Date) });

        if (keywords.Count == 0)
            return new List<ProductSearchItem>();

        // Expand each keyword with singular/plural variants to handle LLM inconsistency
        // e.g. "tim tams" also tries "tim tam"; "biscuit" also tries "biscuits"
        var expandedKeywords = ExpandKeywordVariants(keywords);

        var hitCount = new Dictionary<Guid, int>();
        var productMap = new Dictionary<Guid, PriceCompareData.Entities.Compare.Product>();

        foreach (var keyword in expandedKeywords)
        {
            if (string.IsNullOrWhiteSpace(keyword)) continue;

            var kw = keyword.ToLowerInvariant();

            var query = _db.Products
                .AsNoTracking()
                .Join(
                    latestPerShop,
                    p => new { p.ShopType, Date = p.LastSeenAt.Date },
                    l => new { l.ShopType, Date = l.MaxDate },
                    (p, l) => p)
                .Where(p => p.NormalizedName != null && EF.Functions.ILike(p.NormalizedName, $"%{kw}%"));

            if (shopTypeFilter.HasValue)
                query = query.Where(p => p.ShopType == shopTypeFilter.Value);

            var matches = await query.ToListAsync();

            _logger.LogInformation(
                "DB search for term '{Term}': {Count} matches", kw, matches.Count);

            foreach (var product in matches)
            {
                productMap[product.ProductId] = product;
                hitCount[product.ProductId] = hitCount.GetValueOrDefault(product.ProductId) + 1;
            }
        }

        if (productMap.Count == 0)
            return new List<ProductSearchItem>();

        var ranked = productMap.Values
            .OrderByDescending(p => hitCount[p.ProductId])
            .ThenByDescending(p => p.LastSeenAt)
            .Take(topN)
            .ToList();

        var nameSet = ranked.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var priceRows = await _db.PriceHistory
            .Where(ph => nameSet.Contains(ph.Name!) && ph.ShopType != null)
            .GroupBy(ph => new { ph.Name, ph.ShopType })
            .Select(g => new
            {
                g.Key.Name,
                g.Key.ShopType,
                LatestPrice = g.OrderByDescending(ph => ph.ScrapedAt)
                               .Select(ph => (decimal?)ph.CurrentPrice)
                               .FirstOrDefault(),
                LatestPromoText = g.OrderByDescending(ph => ph.ScrapedAt)
                                   .Select(ph => ph.PromoText)
                                   .FirstOrDefault()
            })
            .ToListAsync();

        var priceMap = priceRows.ToDictionary(
            r => (r.Name?.ToLowerInvariant() ?? "", r.ShopType ?? -1),
            r => (r.LatestPrice, r.LatestPromoText));

        return ranked.Select(p =>
        {
            var key = (p.Name?.ToLowerInvariant() ?? "", (int)(p.ShopType ?? -1));
            priceMap.TryGetValue(key, out var priceInfo);

            return new ProductSearchItem
            {
                ProductId  = p.ProductId,
                Name       = p.Name ?? string.Empty,
                ShopType   = p.ShopType,
                Brand      = p.Brand,
                SizeValue  = p.SizeValue,
                SizeUnit   = p.SizeUnit,
                ImageUrl   = p.ImageUrl,
                Price      = priceInfo.LatestPrice,
                PromoText  = priceInfo.LatestPromoText
            };
        }).ToList();
    }

    /// <summary>
    /// For each keyword, adds singular/plural variants so that "tim tams" also matches "tim tam"
    /// and vice versa. Preserves original order; deduplicated.
    /// </summary>
    private static List<string> ExpandKeywordVariants(List<string> keywords)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var kw in keywords)
        {
            var term = kw.ToLowerInvariant().Trim();
            if (string.IsNullOrWhiteSpace(term)) continue;

            if (seen.Add(term))
                result.Add(term);

            // If ends with 's', add the de-pluralised form
            if (term.EndsWith('s') && term.Length > 1)
            {
                var singular = term[..^1];
                if (seen.Add(singular))
                    result.Add(singular);
            }
            else
            {
                // Add the pluralised form as well
                var plural = term + "s";
                if (seen.Add(plural))
                    result.Add(plural);
            }
        }

        return result;
    }
}
