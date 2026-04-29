using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Interfaces;
using PriceCompareData.Data;

namespace PriceCompareCore.Services
{
    public class CategoryMappingService : ICategoryMappingService
    {
        private readonly AppDbContext _dbContext;
        private readonly ILogger<CategoryMappingService>? _logger;
        private List<PriceCompareData.Entities.Compare.CategoryKeyword>? _keywordCache;

        public CategoryMappingService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public CategoryMappingService(AppDbContext dbContext, ILogger<CategoryMappingService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        // Parse size and package quantity from product name
        public (decimal? sizeValue, string? sizeUnit, int? pkgQty) ParseSpec(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return (null, null, null);
            }
            var lower = name.ToLower();

            // Extract package quantity
            var pkg = Regex.Match(lower, @"(?<![a-z0-9])(\d+)\s?(pack|pk|x)(?![a-z0-9])");
            int? pkgQty = pkg.Success ? int.Parse(pkg.Groups[1].Value) : null;

            // Extract size value and unit
            var m = Regex.Match(lower, @"(\d+(?:\.\d+)?)\s?(l|ml|kg|g)\b");
            if (m.Success)
            {
                var val = decimal.Parse(m.Groups[1].Value);
                var unit = m.Groups[2].Value;

                // Handle unit conversions
                return (val, unit, pkgQty);
            }

            return (null, null, pkgQty);
        }

        // Try to map categoryId by name and brand, return null if not found
        public int? MapCategoryId(string name, string? brand = null)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;

            var text = (brand is null ? name : $"{brand} {name}").ToLowerInvariant();
            var tokens = Regex.Split(text, @"[^a-z0-9]+")
                          .Where(t => !string.IsNullOrWhiteSpace(t))
                          .ToHashSet();
            _keywordCache ??= _dbContext.CategoryKeywords.AsNoTracking().ToList();
            var keywords = _keywordCache;

            var scoreByCat = new Dictionary<int, int>();
            foreach (var kw in keywords)
            {
                var k = kw.Keyword.ToLowerInvariant();
                bool hit = k.Contains(' ') ? text.Contains(k) : tokens.Contains(k);
                if (!hit)
                {
                    continue;
                }
                if (!scoreByCat.ContainsKey(kw.CategoryId))
                {
                    scoreByCat[kw.CategoryId] = 0;
                }
                scoreByCat[kw.CategoryId] += kw.Weight;
            }
            if (scoreByCat.Count == 0)
            {
                return null;
            }
            return scoreByCat.OrderByDescending(x => x.Value).First().Key;
        }
    }
}