using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PriceCompareData.Entities.Compare;
using PriceCompareData.Entities.History;

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
