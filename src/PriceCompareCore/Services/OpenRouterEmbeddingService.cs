using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Services
{
    // Calls OpenRouter embeddings API to generate vectors.
    public class OpenRouterEmbeddingService : IEmbeddingService
    {
        private static readonly ConcurrentDictionary<string, float[]> Cache = new(StringComparer.Ordinal);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly OpenRouterOptions _options;
        private readonly ILogger<OpenRouterEmbeddingService> _logger;

        public OpenRouterEmbeddingService(
            IHttpClientFactory httpClientFactory,
            IOptions<OpenRouterOptions> options,
            ILogger<OpenRouterEmbeddingService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<float[]?> EmbedAsync(Product product)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey) ||
                string.IsNullOrWhiteSpace(_options.EmbeddingModel))
            {
                // Embeddings are disabled until a model/key is configured.
                return null;
            }

            var cacheKey = product.ProductId != Guid.Empty
                ? product.ProductId.ToString()
                : $"{product.ShopType}:{product.NormalizedName}";

            if (Cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var input = BuildEmbeddingInput(product);
            if (string.IsNullOrWhiteSpace(input))
            {
                return null;
            }

            var request = new EmbeddingRequest
            {
                model = _options.EmbeddingModel,
                input = input
            };

            var client = _httpClientFactory.CreateClient("OpenRouter");
            using var http = new HttpRequestMessage(HttpMethod.Post, "embeddings");
            http.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

            if (!string.IsNullOrWhiteSpace(_options.Referer))
            {
                http.Headers.TryAddWithoutValidation("HTTP-Referer", _options.Referer);
            }

            if (!string.IsNullOrWhiteSpace(_options.Title))
            {
                http.Headers.TryAddWithoutValidation("X-Title", _options.Title);
            }

            http.Content = new StringContent(
                JsonSerializer.Serialize(request),
                Encoding.UTF8,
                "application/json");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                var resp = await client.SendAsync(http, cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OpenRouter embeddings returned {Status}.", (int)resp.StatusCode);
                    return null;
                }

                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                var parsed = JsonSerializer.Deserialize<EmbeddingResponse>(json);
                var embedding = parsed?.data?.FirstOrDefault()?.embedding;
                if (embedding == null || embedding.Length == 0)
                {
                    return null;
                }

                Cache[cacheKey] = embedding;
                _logger.LogInformation("Embedding generated product={ProductId} len={Length}", product.ProductId, embedding.Length);
                return embedding;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenRouter embedding request failed.");
                return null;
            }
        }

        private static string BuildEmbeddingInput(Product p)
        {
            var parts = new[]
            {
                p.Name,
                p.SizeValue.HasValue && !string.IsNullOrWhiteSpace(p.SizeUnit)
                    ? $"{p.SizeValue}{p.SizeUnit}"
                    : null,
                p.CategoryId.HasValue ? $"Category {p.CategoryId}" : null
            };

            return string.Join(" ", parts.Where(x => !string.IsNullOrWhiteSpace(x)));
        }

        private sealed class EmbeddingRequest
        {
            public string model { get; set; } = string.Empty;
            public object input { get; set; } = string.Empty;
        }

        private sealed class EmbeddingResponse
        {
            public EmbeddingData[]? data { get; set; }
        }

        private sealed class EmbeddingData
        {
            public float[]? embedding { get; set; }
        }
    }
}
