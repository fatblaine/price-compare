using System;
using System.Collections.Generic;
using System.IO;
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
        private readonly LlmLogOptions _logOptions;

        public OpenRouterMatchVerificationService(
            IHttpClientFactory httpClientFactory,
            IOptions<OpenRouterOptions> options,
            ILogger<OpenRouterMatchVerificationService> logger,
            IOptions<LlmLogOptions> logOptions)
        {
            _httpClientFactory = httpClientFactory;
            _options = options.Value;
            _logger = logger;
            _logOptions = logOptions.Value;
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

            // rawContent 保存 LLM 返回的原始字符串，用于写日志
            var rawContent = string.Empty;
            string decision;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                var resp = await client.SendAsync(http, cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("OpenRouter returned {Status}.", (int)resp.StatusCode);
                    decision = "uncertain";
                }
                else
                {
                    var json = await resp.Content.ReadAsStringAsync(cts.Token);
                    var parsed = JsonSerializer.Deserialize<ChatResponse>(json);
                    // 保留原始返回（未 normalize），便于日志中排查 LLM 返回异常值
                    rawContent = parsed?.choices?.FirstOrDefault()?.message?.content?.Trim() ?? string.Empty;
                    decision = NormalizeDecision(rawContent.ToLowerInvariant());
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OpenRouter match verification failed.");
                decision = "uncertain";
            }

            // 写 JSONL 日志，失败不影响主匹配流程
            try
            {
                await AppendLlmLogAsync(source, top, system, user, rawContent, decision);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM log write failed, ignoring.");
            }

            return decision;
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

        // 把单次 LLM 调用的完整信息追加写入当天的 JSONL 文件
        // 每行一个独立 JSON 对象，方便 jq 等工具逐行处理
        private async Task AppendLlmLogAsync(
            Product source,
            IReadOnlyList<Product> candidates,
            string systemPrompt,
            string userPrompt,
            string rawResponse,
            string decision)
        {
            var record = new
            {
                timestamp   = DateTime.UtcNow,
                model       = _options.Model,
                prompt      = new { system = systemPrompt, user = userPrompt },
                rawResponse,                          // LLM 原始返回，未经 normalize
                decision,                             // normalize 后的最终决定
                source      = MapProduct(source),
                candidates  = candidates.Select(MapProduct)
            };

            // WriteIndented=false 保证每条记录是单行，符合 JSONL 格式
            var line = JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = false });

            var dir  = _logOptions.OutputDir;
            var file = Path.Combine(dir, $"{DateTime.UtcNow:yyyy-MM-dd}.jsonl");

            // 目录不存在时自动创建，幂等
            Directory.CreateDirectory(dir);

            // AppendAllTextAsync：open → write → close，无需持有文件句柄，天然避免锁问题
            await File.AppendAllTextAsync(file, line + "\n");
        }

        // 只提取日志需要的字段，不序列化整个 EF 实体（避免循环引用和冗余数据）
        private static object MapProduct(Product p) => new
        {
            productId = p.ProductId,
            name      = p.Name,
            brand     = p.Brand,
            shopType  = p.ShopType,
            sizeValue = p.SizeValue,
            sizeUnit  = p.SizeUnit
        };

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
