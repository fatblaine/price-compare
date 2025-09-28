using Amazon.DynamoDBv2.DataModel;
using System;
using System.Collections.Generic;

namespace PriceCompareData.DynamoDb
{
    public class DynamoModels
    {

    }

    // Products table
    [DynamoDBTable("Products")]
    public class ProductItem
    {
        // Use unified primary key: ShopType#(SourceId or Name)
        [DynamoDBHashKey] public string ProductPk { get; set; } = default!;

        // Business fields
        public int ShopType { get; set; }
        public string? SourceId { get; set; }
        public string Name { get; set; } = default!;
        public string? Brand { get; set; }
        public decimal? CurrentPrice { get; set; }
        public decimal? OriginalPrice { get; set; }
        public double? SizeValue { get; set; }
        public string? SizeUnit { get; set; }
        public int? PackageQty { get; set; }
        public string? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public string? ImageUrl { get; set; }
        public DateTime LastSeenAt { get; set; }
        public List<string>? Keywords { get; set; }
    }

    // PriceHistory table: One product can have multiple historical prices, sorted by time
    [DynamoDBTable("PriceHistory")]
    public class PriceHistoryItem
    {
        [DynamoDBHashKey] public string ProductPk { get; set; } = default!;
        [DynamoDBRangeKey] public string ScrapedAt { get; set; } = default!;
        public decimal Price { get; set; }
        public int ShopType { get; set; }
        public string? Name { get; set; }
    }

    // CategoryKeywords
    [DynamoDBTable("CategoryKeywords")]
    public class CategoryKeywordItem
    {
        [DynamoDBHashKey] public string CategoryId { get; set; } = default!;
        [DynamoDBRangeKey] public string Keyword { get; set; } = default!;
        public int Weight { get; set; }
    }

    [DynamoDBTable("Categories")]
    public class CategoryItem
    {
        [DynamoDBHashKey] public string CategoryId { get; set; } = default!;
        public string CategoryName { get; set; } = default!;
        public string? Description { get; set; }
    }
}