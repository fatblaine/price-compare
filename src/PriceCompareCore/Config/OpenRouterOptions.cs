using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareCore.Config
{
    // Strongly-typed config for OpenRouter.
    public class OpenRouterOptions
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "";
        public string EmbeddingModel { get; set; } = "";
        public int TimeoutSeconds { get; set; } = 20;
        public int MaxTokens { get; set; } = 256;
        public double Temperature { get; set; } = 0.2;
        public int EmbeddingCandidatePool { get; set; } = 100;
        public string? Referer { get; set; }
        public string? Title { get; set; }
    }
}