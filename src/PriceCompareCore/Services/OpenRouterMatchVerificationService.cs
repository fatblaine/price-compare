using System;
using System.Collections.Generic;
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
    // Uses OpenRouter chat completions to validate low-confidence matches.
    public class OpenRouterMatchVerificationService : IMatchVerificationService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly OpenRouterOptions _options;
        private readonly ILogger<OpenRouterMatchVerificationService> _logger;

        public OpenRouterMatchVerificationService(
            IHttpClientFactory httpClientFactory,
            IOptions<OpenRouterOptions> options,
            ILogger<OpenRouterMatchVerificationService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<string> VerifyAsync(Product source, IReadOnlyList<Product> candidates)
        {
            if (string.IsNullOrWhiteSpace(_options.ApiKey) || string.IsNullOrWhiteSpace(_options.Model))
            {
                return "uncertain";
            }

            if (candidates.Count == 0)
            {
                return "uncertain";
            }

            var top = candidates.Take(5).ToList();

            var system = "You are a product matching validator. Reply with exactly one token: same_product, comparable, no, or uncertain.";
            var user = BuildPrompt(source, top);

            var request = new ChatRequest
            {
                model = _options.Model,
                temperature = _options.Temperature,
                max_tokens = _options.MaxTokens,
                messages = new[]
                {
                    new ChatMessage { role = "system", content = system },
                    new ChatMessage { role = "user", content = user }
                }
            };

            var client = _httpClientFactory.CreateClient("OpenRouter");
            using var http = new HttpRequestMessage(HttpMethod.Post, "chat/completions");

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
                    _logger.LogWarning("OpenRouter returned {Status}.", (int)resp.StatusCode);
                    return "uncertain";
                }

                var json = await resp.Content.ReadAsStringAsync(cts.Token);
                var parsed = JsonSerializer.Deserialize<ChatResponse>(json);

                var content = parsed?.choices?.FirstOrDefault()?.message?.content?.Trim()?.ToLowerInvariant();
                return NormalizeDecision(content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenRouter match verification failed.");
                return "uncertain";
            }
        }

        private static string BuildPrompt(Product source, IReadOnlyList<Product> candidates)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Source product:");
            sb.AppendLine(Describe(source));
            sb.AppendLine();
            sb.AppendLine("Candidates:");
            for (var i = 0; i < candidates.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {Describe(candidates[i])}");
            }
            sb.AppendLine();
            sb.AppendLine("Decide if the best candidate is the same product, comparable, no, or uncertain.");
            return sb.ToString();
        }

        private static string Describe(Product p)
        {
            return $"{p.Brand} {p.Name} | Size {p.SizeValue}{p.SizeUnit} | Category {p.CategoryId}";
        }

        private static string NormalizeDecision(string? raw)
        {
            if (raw == "same_product" || raw == "comparable" || raw == "no" || raw == "uncertain")
            {
                return raw;
            }

            return "uncertain";
        }

        private sealed class ChatRequest
        {
            public string model { get; set; } = string.Empty;
            public double temperature { get; set; }
            public int max_tokens { get; set; }
            public ChatMessage[] messages { get; set; } = Array.Empty<ChatMessage>();
        }

        private sealed class ChatMessage
        {
            public string role { get; set; } = string.Empty;
            public string content { get; set; } = string.Empty;
        }

        private sealed class ChatResponse
        {
            public List<ChatChoice>? choices { get; set; }
        }

        private sealed class ChatChoice
        {
            public ChatMessage? message { get; set; }
        }
    }
}
