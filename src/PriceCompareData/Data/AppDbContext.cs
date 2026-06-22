using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareData.Entities;
using PriceCompareData.Entities.Compare;
using PriceCompareData.Entities.History;
using PriceCompareData.Entities.Jobs;
using PriceCompareData.Entities.Receipts;
using PriceCompareData.Entities.Users;

namespace PriceCompareData.Data
{
      public class AppDbContext : DbContext
      {
            public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
            {
            }

            public DbSet<PriceHistory> PriceHistory { get; set; }
            public DbSet<Category> Categories => Set<Category>();
            public DbSet<CategoryKeyword> CategoryKeywords => Set<CategoryKeyword>();
            public DbSet<Product> Products => Set<Product>();
            public DbSet<Receipt> Receipts => Set<Receipt>();
            public DbSet<ReceiptItem> ReceiptItems => Set<ReceiptItem>();
            public DbSet<FavoriteItem> FavoriteItems => Set<FavoriteItem>();
            public DbSet<FavoritePriceAlert> FavoritePriceAlerts => Set<FavoritePriceAlert>();
            public DbSet<User> Users { get { return Set<User>(); } }
            public DbSet<JobDefinition> JobDefinitions => Set<JobDefinition>();
            public DbSet<JobRun> JobRuns => Set<JobRun>();
            public DbSet<ProductMatch> ProductMatches => Set<ProductMatch>();
            public DbSet<MatchJob> MatchJobs => Set<MatchJob>();



            // Configure entity relationships and constraints
            protected override void OnModelCreating(ModelBuilder modelBuilder)
            {
                  base.OnModelCreating(modelBuilder);

                  // Composite index on ShopType and SourceId for quick lookups
                  modelBuilder.Entity<Product>()
                      .HasIndex(p => new { p.ShopType, p.SourceId });
                  // Index on Name for search optimization
                  modelBuilder.Entity<Product>()
                      .HasIndex(p => new { p.ShopType, p.Name });
                  // Index on CategoryId, SizeUnit, SizeValue for category and size-based queries
                  modelBuilder.Entity<Product>()
                      .HasIndex(p => new { p.CategoryId, p.SizeUnit, p.SizeValue });
                  modelBuilder.Entity<Product>().HasIndex(p => new { p.ShopType, p.NormalizedName });
                  modelBuilder.Entity<Product>()
                      .HasIndex(p => new { p.CategoryId, p.SizeStandardUnit, p.SizeStandardValue });
                  // Index to support per-shop latest-scrape-date filter in ProductService
                  modelBuilder.Entity<Product>()
                      .HasIndex(p => new { p.ShopType, p.LastSeenAt })
                      .HasDatabaseName("IX_Products_ShopType_LastSeenAt");

                  // ProductMatch
                  modelBuilder.Entity<ProductMatch>(entity =>
                  {
                        entity.ToTable("productmatch");
                        entity.HasKey(e => e.Id);

                        // Index for same_product sort: SELECT sourceproductid WHERE matchtype = 'same_product'
                        entity.HasIndex(e => new { e.MatchType, e.SourceProductId })
                              .HasDatabaseName("IX_ProductMatch_MatchType_SourceProductId");

                        entity.Property(e => e.Id).HasColumnName("id");
                        entity.Property(e => e.SourceProductId).HasColumnName("sourceproductid");
                        entity.Property(e => e.TargetProductId).HasColumnName("targetproductid");

                        entity.Property(e => e.SourceShopType).HasColumnName("sourceshoptype");
                        entity.Property(e => e.TargetShopType).HasColumnName("targetshoptype");

                        entity.Property(e => e.Score).HasColumnName("score");
                        entity.Property(e => e.Method).HasColumnName("method");
                        entity.Property(e => e.MatchType).HasColumnName("matchtype");

                        entity.Property(e => e.IsConfirmed).HasColumnName("isconfirmed");
                        var evidence = entity.Property(e => e.EvidenceJson)
                              .HasColumnName("evidencejson");
                        // Npgsql maps JsonDocument natively to jsonb. The InMemory provider
                        // (used in tests) has no JsonDocument mapping since EF Core 10, so
                        // convert to its raw text there. EF skips the converter for null values.
                        if (Database.ProviderName == "Microsoft.EntityFrameworkCore.InMemory")
                        {
                              evidence.HasConversion(
                                    v => v!.RootElement.GetRawText(),
                                    v => JsonDocument.Parse(v, default));
                        }
                        else
                        {
                              evidence.HasColumnType("jsonb");
                        }

                        entity.Property(e => e.CreatedAt).HasColumnName("createdat");
                        entity.Property(e => e.UpdatedAt).HasColumnName("updatedat");
                  });

                  // MatchJob
                  modelBuilder.Entity<MatchJob>(entity =>
                  {
                        entity.ToTable("matchjob");
                        entity.HasKey(e => e.Id);

                        entity.Property(e => e.Id).HasColumnName("id");
                        entity.Property(e => e.SourceShop).HasColumnName("sourceshop");
                        entity.Property(e => e.TargetShop).HasColumnName("targetshop");
                        entity.Property(e => e.Status).HasColumnName("status");
                        entity.Property(e => e.Mode).HasColumnName("mode");
                        entity.Property(e => e.Since).HasColumnName("since");

                        entity.Property(e => e.Total).HasColumnName("total");
                        entity.Property(e => e.Processed).HasColumnName("processed");
                        entity.Property(e => e.Matched).HasColumnName("matched");
                        entity.Property(e => e.Comparable).HasColumnName("comparable");
                        entity.Property(e => e.Failed).HasColumnName("failed");

                        entity.Property(e => e.UseLlm).HasColumnName("usellm");
                        entity.Property(e => e.LimitNum).HasColumnName("limitnum");
                        entity.Property(e => e.TopN).HasColumnName("topn");

                        entity.Property(e => e.ErrorMessage).HasColumnName("errormessage");
                        entity.Property(e => e.CreatedAt).HasColumnName("createdat");
                        entity.Property(e => e.UpdatedAt).HasColumnName("updatedat");
                  });


                  // PriceHistory index for latest-price lookups: WHERE shoptype = X AND name = 'Y' ORDER BY scrapedat DESC
                  modelBuilder.Entity<PriceHistory>()
                      .HasIndex(p => new { p.ShopType, p.Name, p.ScrapedAt })
                      .IsDescending(false, false, true)
                      .HasDatabaseName("IX_PriceHistory_ShopType_Name_ScrapedAt");

                  // PriceHistory
                  modelBuilder.Entity<PriceHistory>()
                      .Property(p => p.PromoText)
                      .HasColumnName("promotext");

                  // JobDefinition
                  modelBuilder.Entity<JobDefinition>(entity =>
                  {
                        entity.ToTable("job_definitions");
                        entity.HasKey(e => new { e.JobName, e.Source });
                        entity.Property(e => e.JobName).HasColumnName("job_name");
                        entity.Property(e => e.Source).HasColumnName("source");
                        entity.Property(e => e.ScheduleExpression).HasColumnName("schedule_expression");
                        entity.Property(e => e.Timezone).HasColumnName("timezone");
                        entity.Property(e => e.Enabled).HasColumnName("enabled").HasDefaultValue(true);
                        entity.Property(e => e.Description).HasColumnName("description");
                        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                        entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");
                  });

                  // JobRun
                  modelBuilder.Entity<JobRun>(entity =>
                  {
                        entity.ToTable("job_runs");
                        entity.HasKey(e => e.Id);
                        entity.Property(e => e.Id).HasColumnName("id");
                        entity.Property(e => e.JobName).HasColumnName("job_name");
                        entity.Property(e => e.Source).HasColumnName("source");
                        entity.Property(e => e.ScheduledTime).HasColumnName("scheduled_time");
                        entity.Property(e => e.StartTime).HasColumnName("start_time");
                        entity.Property(e => e.EndTime).HasColumnName("end_time");
                        entity.Property(e => e.Status).HasColumnName("status");
                        entity.Property(e => e.DurationMs).HasColumnName("duration_ms");
                        entity.Property(e => e.ErrorMessage).HasColumnName("error_message");
                        entity.Property(e => e.RequestId).HasColumnName("request_id");
                        entity.Property(e => e.Environment).HasColumnName("environment");
                        entity.Property(e => e.CreatedAt).HasColumnName("created_at");
                  });

                  modelBuilder.Entity<Receipt>()
                  .HasMany(r => r.Items)
                  .WithOne(i => i.Receipt!)
                  .HasForeignKey(i => i.ReceiptId)
                  .OnDelete(DeleteBehavior.Cascade);

                  modelBuilder.Entity<FavoriteItem>()
                  .HasIndex(f => new { f.UserId, f.ProductId })
                  .IsUnique();

                  // Receipt
                  modelBuilder.Entity<Receipt>(entity =>
                  {
                        entity.ToTable("receipt");
                        entity.HasKey(e => e.Id).HasName("pk_receipt");
                        entity.Property(e => e.Id).HasColumnName("id");
                        entity.Property(e => e.UserId).HasColumnName("userid");
                        entity.Property(e => e.StoreName).HasColumnName("storename");
                        entity.Property(e => e.PurchaseDate).HasColumnName("purchasedate");
                        entity.Property(e => e.UploadUrl).HasColumnName("uploadurl");
                        entity.Property(e => e.TotalAmount).HasColumnName("totalamount").HasPrecision(18, 4);
                  });

                  // ReceiptItem
                  modelBuilder.Entity<ReceiptItem>(entity =>
                  {
                        entity.ToTable("receiptitem");
                        entity.HasKey(e => e.Id).HasName("pk_receiptitem");
                        entity.Property(e => e.Id).HasColumnName("id");
                        entity.Property(e => e.ReceiptId).HasColumnName("receiptid");
                        entity.Property(e => e.ProductName).HasColumnName("productname");
                        entity.Property(e => e.OriginalName).HasColumnName("originalname");
                        entity.Property(e => e.Price).HasColumnName("price").HasPrecision(18, 4);
                        entity.Property(e => e.Quantity).HasColumnName("quantity");
                        entity.Property(e => e.MatchedProductId).HasColumnName("matchedproductid");
                        entity.Property(e => e.Confidence).HasColumnName("confidence");
                  });

                  // FavoriteItem
                  modelBuilder.Entity<FavoriteItem>(entity =>
                  {
                        entity.ToTable("favoriteitem");
                        entity.HasKey(e => e.Id).HasName("pk_favoriteitem");
                        entity.Property(e => e.Id).HasColumnName("id");
                        entity.Property(e => e.UserId).HasColumnName("userid");
                        entity.Property(e => e.ProductId).HasColumnName("productid");
                        entity.Property(e => e.IsActive).HasColumnName("isactive").HasDefaultValue(true);
                        entity.Property(e => e.TargetPrice).HasColumnName("targetprice").HasPrecision(18, 4);
                        entity.Property(e => e.NotifyOnAnyDrop).HasColumnName("notifyonanydrop").HasDefaultValue(true);
                        entity.Property(e => e.LastSeenPrice).HasColumnName("lastseenprice").HasPrecision(18, 4);
                        entity.Property(e => e.LastNotifiedPrice).HasColumnName("lastnotifiedprice").HasPrecision(18, 4);
                        entity.Property(e => e.LastNotifiedAt).HasColumnName("lastnotifiedat");
                        entity.Property(e => e.CreatedAt).HasColumnName("createdat");
                  });

                  // FavoritePriceAlert
                  modelBuilder.Entity<FavoritePriceAlert>(entity =>
                  {
                        entity.ToTable("favoritepricealert");
                        entity.HasKey(e => e.Id).HasName("pk_favoritepricealert");
                        entity.Property(e => e.Id).HasColumnName("id");
                        entity.Property(e => e.FavoriteItemId).HasColumnName("favoriteitemid");
                        entity.Property(e => e.OldPrice).HasColumnName("oldprice").HasPrecision(18, 4);
                        entity.Property(e => e.NewPrice).HasColumnName("newprice").HasPrecision(18, 4);
                        entity.Property(e => e.TriggeredAt).HasColumnName("triggeredat");
                        entity.Property(e => e.Status).HasColumnName("status");
                        entity.Property(e => e.ErrorMessage).HasColumnName("errormessage");

                        entity.HasOne(e => e.FavoriteItem)
                              .WithMany()
                              .HasForeignKey(e => e.FavoriteItemId)
                              .HasConstraintName("fk_favoritepricealert_favoriteitem")
                              .OnDelete(DeleteBehavior.Cascade);
                  });

                  // User
                  modelBuilder.Entity<User>(entity =>
                  {
                        entity.ToTable("user");
                        entity.HasKey(e => e.Id).HasName("pk_user");
                        // Keep column names matching existing schema (likely quoted PascalCase).
                        entity.Property(e => e.Id).HasColumnName("Id");
                        entity.Property(e => e.Email).HasColumnName("Email");
                        entity.Property(e => e.PasswordHash).HasColumnName("PasswordHash");
                        entity.Property(e => e.CreatedAt).HasColumnName("CreatedAt");
                        entity.HasIndex(e => e.Email).IsUnique();
                  });

                  // Seed initial categories
                  modelBuilder.Entity<Category>().HasData(
                      new Category { CategoryId = 1, Name = "Milk" },
                      new Category { CategoryId = 2, Name = "Bread" },
                      new Category { CategoryId = 3, Name = "Eggs" },
                      new Category { CategoryId = 4, Name = "Apple" },
                      new Category { CategoryId = 5, Name = "Toothpaste" },
                      new Category { CategoryId = 6, Name = "Rice" },
                      new Category { CategoryId = 7, Name = "Pasta" },
                      new Category { CategoryId = 8, Name = "Shampoo" },
                      new Category { CategoryId = 9, Name = "Soap" },
                      new Category { CategoryId = 10, Name = "Cheese" },
                      new Category { CategoryId = 11, Name = "Chicken" },
                      new Category { CategoryId = 12, Name = "Beef" },
                      new Category { CategoryId = 13, Name = "Fish" },
                      new Category { CategoryId = 14, Name = "Yogurt" },
                      new Category { CategoryId = 15, Name = "Coffee" },
                      new Category { CategoryId = 16, Name = "Tea" },
                      new Category { CategoryId = 17, Name = "Soft Drink" },
                      new Category { CategoryId = 18, Name = "Beer" },
                      new Category { CategoryId = 19, Name = "Wine" },
                      new Category { CategoryId = 20, Name = "Cereal" }
                  );

                  // Keywords for categories
                  modelBuilder.Entity<CategoryKeyword>().HasData(
                      // Milk
                      new CategoryKeyword { KeywordId = 1, CategoryId = 1, Keyword = "milk" },
                      new CategoryKeyword { KeywordId = 2, CategoryId = 1, Keyword = "full cream" },
                      new CategoryKeyword { KeywordId = 3, CategoryId = 1, Keyword = "skim" },
                      new CategoryKeyword { KeywordId = 4, CategoryId = 1, Keyword = "lactose free" },

                      // Bread
                      new CategoryKeyword { KeywordId = 10, CategoryId = 2, Keyword = "bread" },
                      new CategoryKeyword { KeywordId = 11, CategoryId = 2, Keyword = "wholemeal" },
                      new CategoryKeyword { KeywordId = 12, CategoryId = 2, Keyword = "sourdough" },

                      // Eggs
                      new CategoryKeyword { KeywordId = 20, CategoryId = 3, Keyword = "egg" },
                      new CategoryKeyword { KeywordId = 21, CategoryId = 3, Keyword = "free range" },

                      // Apple
                      new CategoryKeyword { KeywordId = 30, CategoryId = 4, Keyword = "apple" },
                      new CategoryKeyword { KeywordId = 31, CategoryId = 4, Keyword = "fuji" },
                      new CategoryKeyword { KeywordId = 32, CategoryId = 4, Keyword = "gala" },

                      // Toothpaste
                      new CategoryKeyword { KeywordId = 40, CategoryId = 5, Keyword = "toothpaste" },
                      new CategoryKeyword { KeywordId = 41, CategoryId = 5, Keyword = "whitening" },
                      new CategoryKeyword { KeywordId = 42, CategoryId = 5, Keyword = "fluoride" },

                      // Rice
                      new CategoryKeyword { KeywordId = 50, CategoryId = 6, Keyword = "rice" },
                      new CategoryKeyword { KeywordId = 51, CategoryId = 6, Keyword = "basmati" },
                      new CategoryKeyword { KeywordId = 52, CategoryId = 6, Keyword = "jasmine" },

                      // Pasta
                      new CategoryKeyword { KeywordId = 60, CategoryId = 7, Keyword = "pasta" },
                      new CategoryKeyword { KeywordId = 61, CategoryId = 7, Keyword = "spaghetti" },
                      new CategoryKeyword { KeywordId = 62, CategoryId = 7, Keyword = "penne" },

                      // Shampoo
                      new CategoryKeyword { KeywordId = 70, CategoryId = 8, Keyword = "shampoo" },
                      new CategoryKeyword { KeywordId = 71, CategoryId = 8, Keyword = "anti-dandruff" },

                      // Soap
                      new CategoryKeyword { KeywordId = 80, CategoryId = 9, Keyword = "soap" },
                      new CategoryKeyword { KeywordId = 81, CategoryId = 9, Keyword = "bar soap" },

                      // Cheese
                      new CategoryKeyword { KeywordId = 90, CategoryId = 10, Keyword = "cheese" },
                      new CategoryKeyword { KeywordId = 91, CategoryId = 10, Keyword = "cheddar" },
                      new CategoryKeyword { KeywordId = 92, CategoryId = 10, Keyword = "mozzarella" },

                      // Chicken
                      new CategoryKeyword { KeywordId = 100, CategoryId = 11, Keyword = "chicken" },
                      new CategoryKeyword { KeywordId = 101, CategoryId = 11, Keyword = "breast fillet" },
                      new CategoryKeyword { KeywordId = 102, CategoryId = 11, Keyword = "drumstick" },

                      // Beef
                      new CategoryKeyword { KeywordId = 110, CategoryId = 12, Keyword = "beef" },
                      new CategoryKeyword { KeywordId = 111, CategoryId = 12, Keyword = "mince" },
                      new CategoryKeyword { KeywordId = 112, CategoryId = 12, Keyword = "steak" },

                      // Fish
                      new CategoryKeyword { KeywordId = 120, CategoryId = 13, Keyword = "fish" },
                      new CategoryKeyword { KeywordId = 121, CategoryId = 13, Keyword = "salmon" },
                      new CategoryKeyword { KeywordId = 122, CategoryId = 13, Keyword = "tuna" },

                      // Yogurt
                      new CategoryKeyword { KeywordId = 130, CategoryId = 14, Keyword = "yogurt" },
                      new CategoryKeyword { KeywordId = 131, CategoryId = 14, Keyword = "greek" },

                      // Coffee
                      new CategoryKeyword { KeywordId = 140, CategoryId = 15, Keyword = "coffee" },
                      new CategoryKeyword { KeywordId = 141, CategoryId = 15, Keyword = "instant" },
                      new CategoryKeyword { KeywordId = 142, CategoryId = 15, Keyword = "espresso" },

                      // Tea
                      new CategoryKeyword { KeywordId = 150, CategoryId = 16, Keyword = "tea" },
                      new CategoryKeyword { KeywordId = 151, CategoryId = 16, Keyword = "green tea" },
                      new CategoryKeyword { KeywordId = 152, CategoryId = 16, Keyword = "black tea" },

                      // Soft Drink
                      new CategoryKeyword { KeywordId = 160, CategoryId = 17, Keyword = "cola" },
                      new CategoryKeyword { KeywordId = 161, CategoryId = 17, Keyword = "soft drink" },
                      new CategoryKeyword { KeywordId = 162, CategoryId = 17, Keyword = "soda" },

                      // Beer
                      new CategoryKeyword { KeywordId = 170, CategoryId = 18, Keyword = "beer" },
                      new CategoryKeyword { KeywordId = 171, CategoryId = 18, Keyword = "lager" },
                      new CategoryKeyword { KeywordId = 172, CategoryId = 18, Keyword = "ale" },

                      // Wine
                      new CategoryKeyword { KeywordId = 180, CategoryId = 19, Keyword = "wine" },
                      new CategoryKeyword { KeywordId = 181, CategoryId = 19, Keyword = "red wine" },
                      new CategoryKeyword { KeywordId = 182, CategoryId = 19, Keyword = "white wine" },

                      // Cereal
                      new CategoryKeyword { KeywordId = 190, CategoryId = 20, Keyword = "cereal" },
                      new CategoryKeyword { KeywordId = 191, CategoryId = 20, Keyword = "cornflakes" },
                      new CategoryKeyword { KeywordId = 192, CategoryId = 20, Keyword = "muesli" }
                  );

                  // Category 
                  modelBuilder.Entity<Category>(entity =>
                  {
                        entity.ToTable("category");
                        entity.HasKey(e => e.CategoryId)
                        .HasName("pk_category");

                        entity.Property(e => e.CategoryId)
                        .HasColumnName("categoryid");

                        entity.Property(e => e.Name)
                        .HasColumnName("name")
                        .IsRequired();

                        entity.Property(e => e.ParentId)
                        .HasColumnName("parentid");
                  });

                  // CategoryKeyword
                  modelBuilder.Entity<CategoryKeyword>(entity =>
                  {
                        entity.ToTable("categorykeyword");
                        entity.HasKey(e => e.KeywordId)
                        .HasName("pk_categorykeyword");

                        entity.Property(e => e.KeywordId)
                        .HasColumnName("keywordid");

                        entity.Property(e => e.CategoryId)
                        .HasColumnName("categoryid");

                        entity.Property(e => e.Keyword)
                        .HasColumnName("keyword")
                        .IsRequired();

                        entity.Property(e => e.Weight)
                        .HasColumnName("weight")
                        .HasDefaultValue(1);

                        // foreign key to Category
                        entity.HasOne<Category>()
                        .WithMany()
                        .HasForeignKey(e => e.CategoryId)
                        .HasConstraintName("fk_categorykeyword_category")
                        .OnDelete(DeleteBehavior.Cascade);
                  });

                  // Product
                  modelBuilder.Entity<Product>(entity =>
                  {
                        entity.ToTable("product");
                        entity.HasKey(e => e.ProductId)
                        .HasName("pk_product");

                        entity.Property(e => e.ProductId)
                        .HasColumnName("productid");

                        entity.Property(e => e.ShopType)
                        .HasColumnName("shoptype");

                        entity.Property(e => e.SourceId)
                        .HasColumnName("sourceid");

                        entity.Property(e => e.Name)
                        .HasColumnName("name")
                        .IsRequired();

                        entity.Property(e => e.Brand)
                        .HasColumnName("brand");

                        entity.Property(e => e.SizeValue)
                        .HasColumnName("sizevalue")
                        .HasPrecision(18, 4);

                        entity.Property(e => e.SizeUnit)
                        .HasColumnName("sizeunit");

                        entity.Property(e => e.PackageQty)
                        .HasColumnName("packageqty");

                        entity.Property(e => e.CategoryId)
                        .HasColumnName("categoryid");

                        entity.Property(e => e.ImageUrl)
                        .HasColumnName("imageurl");

                        entity.Property(e => e.LastSeenAt)
                        .HasColumnName("lastseenat");

                        // foreign key to Category
                        entity.HasOne<Category>()
                        .WithMany()
                        .HasForeignKey(e => e.CategoryId)
                        .HasConstraintName("fk_product_category")
                        .OnDelete(DeleteBehavior.SetNull);

                        entity.Property(e => e.NormalizedName).HasColumnName("normalizedname");
                        entity.Property(e => e.NormalizedBrand).HasColumnName("normalizedbrand");
                        entity.Property(e => e.SizeStandardValue).HasColumnName("sizestandardvalue").HasPrecision(18, 4);
                        entity.Property(e => e.SizeStandardUnit).HasColumnName("sizestandardunit");
                        entity.Property(e => e.SizeUnknown).HasColumnName("sizeunknown");
                        entity.Property(e => e.BrandUnknown).HasColumnName("brandunknown");
                        entity.Property(e => e.CategoryUnknown).HasColumnName("categoryunknown");
                  });

                  // PriceHistory
                  modelBuilder.Entity<PriceHistory>(entity =>
                  {
                        entity.ToTable("pricehistory");
                        entity.HasKey(e => e.Id)
                        .HasName("pk_pricehistory");

                        entity.Property(e => e.Id)
                        .HasColumnName("id");

                        entity.Property(e => e.Name)
                        .HasColumnName("name");

                        entity.Property(e => e.CurrentPrice)
                        .HasColumnName("currentprice")
                        .HasPrecision(18, 4);

                        entity.Property(e => e.ImageUrl)
                        .HasColumnName("imageurl")
                        .IsRequired();

                        entity.Property(e => e.ScrapedAt)
                        .HasColumnName("scrapedat");

                        entity.Property(e => e.OfferType)
                        .HasColumnName("offertype");

                        entity.Property(e => e.ShopType)
                        .HasColumnName("shoptype");
                  });
            }
      }
}
