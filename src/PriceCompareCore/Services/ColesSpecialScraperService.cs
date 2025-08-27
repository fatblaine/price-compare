using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using PriceCompareCore.Interfaces;
using PriceCompareData.DTOs;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Common;

namespace PriceCompareCore.Services
{
    public class ColesSpecialScraperService : IColesSpecialScraperService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ColesSpecialScraperService> _logger;
        private readonly AsyncRetryPolicy _retryPolicy;
        private readonly IDistributedCache _cache;
        private const string BaseUrl = WebInfo.COLES_BASE_URL;

        public ColesSpecialScraperService(HttpClient httpClient, ILogger<ColesSpecialScraperService> logger, IDistributedCache cache)
        {
            _httpClient = httpClient;
            _httpClient.DefaultRequestHeaders
                .Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
            _logger = logger;
            _cache = cache;

            // polly retry policy
            _retryPolicy = Policy
                .Handle<HttpRequestException>()
                .Or<WebException>()
                .WaitAndRetryAsync(3, retryAttempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        Console.WriteLine($"Retry {retryCount} after {timeSpan.TotalSeconds}s due to: {exception.Message}");
                    });
        }

        public Task<List<ColesSpecialProduct>> GetAllOnSpecialProductsAsync(ColesSpecialProductRequest request)
        {
            List<ColesSpecialProduct> products = new List<ColesSpecialProduct>();

            return Task.FromResult(products);
        }
    }
}