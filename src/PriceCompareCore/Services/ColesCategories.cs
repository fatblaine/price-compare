using System;
using System.Collections.Generic;
using System.Linq;
using PriceCompareData.Common;

namespace PriceCompareCore.Services
{
    /// <summary>
    /// One Coles browse category scraped through <c>/browse/{Slug}</c>.
    /// </summary>
    /// <param name="Slug">Coles URL slug, e.g. <c>bakery</c>.</param>
    /// <param name="OfferType">Value written to <c>pricehistory.offertype</c>.</param>
    /// <param name="JobName">Quartz job name; kept stable so <c>job_runs</c> history carries over.</param>
    /// <param name="Cron">Quartz cron in the server's local time zone (Australia/Sydney).</param>
    /// <param name="ExportSource">Folder/source name used by the scrape export.</param>
    /// <param name="EnvPrefix">Prefix for per-category overrides, e.g. <c>COLES_BAKERY</c>_MAX_PAGES.</param>
    public sealed record ColesCategory(
        string Slug,
        int OfferType,
        string JobName,
        string Cron,
        string ExportSource,
        string EnvPrefix)
    {
        public const int DefaultMaxPages = 150;
        public const int DefaultMaxItems = 7000;

        public int MaxPages => ReadLimit("MAX_PAGES", DefaultMaxPages);

        public int MaxItems => ReadLimit("MAX_ITEMS", DefaultMaxItems);

        private int ReadLimit(string suffix, int fallback)
        {
            var raw = Environment.GetEnvironmentVariable($"{EnvPrefix}_{suffix}");
            if (int.TryParse(raw, out var v) && v > 0)
            {
                return v;
            }

            raw = Environment.GetEnvironmentVariable($"COLES_DOM_{suffix}");
            return int.TryParse(raw, out v) && v > 0 ? v : fallback;
        }
    }

    /// <summary>
    /// The single list of Coles categories we scrape. Adding or retiring a category is one line here
    /// (plus the job_definitions seed script). Checked against coles.com.au/browse on 2026-10-07.
    /// Not scraped: back-to-school (seasonal, duplicates other categories) and tobacco (no online range).
    /// </summary>
    public static class ColesCategories
    {
        public static IReadOnlyList<ColesCategory> All { get; } = new[]
        {
            Create("health-dietary",          OfferType.HEALTH_DIETARY,          "ColesHealthDietaryDomJob",          "0 5 0 ? * WED"),
            Create("meat-seafood",            OfferType.MEAT_SEAFOOD,            "ColesMeatSeafoodDomJob",            "0 15 0 ? * WED"),
            Create("fruit-vegetables",        OfferType.FRUIT_VEGETABLES,        "ColesFruitVegetablesDomJob",        "0 25 0 ? * WED"),
            Create("dairy-eggs-fridge",       OfferType.DAIRY_EGGS_FRIDGE,       "ColesDairyEggsFridgeDomJob",        "0 35 0 ? * WED"),
            Create("bakery",                  OfferType.BAKERY,                  "ColesBakeryDomJob",                 "0 45 0 ? * WED"),
            Create("deli",                    OfferType.DELI,                    "ColesDeliDomJob",                   "0 55 0 ? * WED"),
            Create("pantry",                  OfferType.PANTRY,                  "ColesPantryDomJob",                 "0 5 1 ? * WED"),
            Create("international-foods",     OfferType.INTERNATIONAL_FOODS,     "ColesInternationalFoodsDomJob",     "0 15 1 ? * WED"),
            Create("chips-chocolates-snacks", OfferType.CHIPS_CHOCOLATES_SNACKS, "ColesChipsChocolatesSnacksDomJob",  "0 25 1 ? * WED"),
            Create("drinks",                  OfferType.DRINKS,                  "ColesDrinksDomJob",                 "0 35 1 ? * WED"),
            Create("liquorland",              OfferType.LIQUORLAND,              "ColesLiquorlandDomJob",             "0 45 1 ? * WED"),
            Create("frozen",                  OfferType.FROZEN,                  "ColesFrozenDomJob",                 "0 55 1 ? * WED"),
            Create("cleaning-laundry",        OfferType.CLEANING_LAUNDRY,        "ColesCleaningLaundryDomJob",        "0 5 2 ? * WED"),
            Create("health-beauty",           OfferType.HEALTH_BEAUTY,           "ColesHealthBeautyDomJob",           "0 15 2 ? * WED"),
            Create("baby",                    OfferType.BABY,                    "ColesBabyDomJob",                   "0 25 2 ? * WED"),
            Create("pet",                     OfferType.PET,                     "ColesPetDomJob",                    "0 35 2 ? * WED"),
            Create("home-garden",             OfferType.HOME_GARDEN,             "ColesHomeGardenDomJob",             "0 45 2 ? * WED"),
            Create("big-pack-value",          OfferType.BIG_PACK_VALUE,          "ColesBigPackValueDomJob",           "0 55 2 ? * WED"),
            Create("bonus-credit-products",   OfferType.BONUS_CREDIT_PRODUCTS,   "ColesBonusCreditProductsDomJob",    "0 5 3 ? * WED"),
            Create("deliver-more-range",      OfferType.DELIVER_MORE_RANGE,      "ColesDeliverMoreRangeDomJob",       "0 15 3 ? * WED"),
        };

        public static ColesCategory? FindBySlug(string? slug) =>
            All.FirstOrDefault(c => string.Equals(c.Slug, slug, StringComparison.OrdinalIgnoreCase));

        private static ColesCategory Create(string slug, int offerType, string jobName, string cron)
        {
            var snake = slug.Replace('-', '_');
            return new ColesCategory(
                slug,
                offerType,
                jobName,
                cron,
                $"coles_{snake}_json",
                $"COLES_{snake.ToUpperInvariant()}");
        }
    }
}
