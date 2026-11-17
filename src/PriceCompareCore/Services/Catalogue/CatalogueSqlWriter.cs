using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PriceCompareCore.Utils;

namespace PriceCompareCore.Services.Catalogue
{
    /// <summary>
    /// Builds one transaction that stores a catalogue and matches its SKUs to products (BTS-163).
    /// Matching is SQL in the target database on purpose: product ids differ between dev and prod,
    /// so the same script can run on Lambda (prod) or be exported and applied elsewhere.
    /// </summary>
    public static class CatalogueSqlWriter
    {
        /// <summary>Keep this many days of past catalogues (≈ 8 weekly editions).</summary>
        public const int RetentionDays = 56;

        private const int RowsPerInsert = 500;

        public static string Build(
            int shopType,
            int saleId,
            string region,
            DateOnly validFrom,
            DateOnly validTo,
            DateTime scrapedAtUtc,
            IReadOnlyList<CatalogueItemWithSkus> items)
        {
            if (shopType is not (0 or 1))
            {
                throw new ArgumentOutOfRangeException(nameof(shopType), shopType, "Expected 0 (Coles) or 1 (Woolworths).");
            }

            var sb = new StringBuilder();
            var shop = SqlLiteral.Int(shopType);
            var sale = SqlLiteral.Int(saleId);

            sb.AppendLine($"-- BTS-163 next-week catalogue: shoptype {shop}, sale {sale}, {items.Count} items, valid {validFrom:yyyy-MM-dd}..{validTo:yyyy-MM-dd}");
            sb.AppendLine("BEGIN;");
            sb.AppendLine();

            // (a) items
            foreach (var chunk in items.Chunk(RowsPerInsert))
            {
                sb.AppendLine("INSERT INTO catalogue_special (shoptype, saleid, region, itemid, name, price, priceunit, wasprice,");
                sb.AppendLine("    saveamount, unitpricetext, promotext, imageurl, validfrom, validto, scrapedat)");
                sb.AppendLine("VALUES");
                for (var i = 0; i < chunk.Length; i++)
                {
                    var it = chunk[i].Item;
                    sb.Append("    (").Append(shop).Append(", ").Append(sale).Append(", ")
                      .Append(SqlLiteral.Text(region)).Append(", ")
                      .Append(SqlLiteral.Text(it.ItemId)).Append(", ")
                      .Append(SqlLiteral.Text(it.Name)).Append(", ")
                      .Append(SqlLiteral.Decimal(it.Price)).Append(", ")
                      .Append(SqlLiteral.Text(it.PriceUnit)).Append(", ")
                      .Append(SqlLiteral.Decimal(it.WasPrice)).Append(", ")
                      .Append(SqlLiteral.Decimal(it.SaveAmount)).Append(", ")
                      .Append(SqlLiteral.Text(it.UnitPriceText)).Append(", ")
                      .Append(SqlLiteral.Text(it.PromoText)).Append(", ")
                      .Append(SqlLiteral.Text(it.ImageUrl)).Append(", ")
                      .Append(SqlLiteral.Date(validFrom)).Append(", ")
                      .Append(SqlLiteral.Date(validTo)).Append(", ")
                      .Append(SqlLiteral.TimestampUtc(scrapedAtUtc)).Append(')')
                      .AppendLine(i == chunk.Length - 1 ? string.Empty : ",");
                }

                sb.AppendLine("ON CONFLICT (shoptype, saleid, itemid) DO UPDATE SET");
                sb.AppendLine("    region = EXCLUDED.region, name = EXCLUDED.name, price = EXCLUDED.price, priceunit = EXCLUDED.priceunit,");
                sb.AppendLine("    wasprice = EXCLUDED.wasprice, saveamount = EXCLUDED.saveamount, unitpricetext = EXCLUDED.unitpricetext,");
                sb.AppendLine("    promotext = EXCLUDED.promotext, imageurl = EXCLUDED.imageurl,");
                sb.AppendLine("    validfrom = EXCLUDED.validfrom, validto = EXCLUDED.validto, scrapedat = EXCLUDED.scrapedat;");
                sb.AppendLine();
            }

            // Items dropped from the catalogue since an earlier run of the same sale.
            var itemIds = items.Select(i => SqlLiteral.Text(i.Item.ItemId)).ToList();
            sb.Append("DELETE FROM catalogue_special WHERE shoptype = ").Append(shop).Append(" AND saleid = ").Append(sale);
            sb.AppendLine(itemIds.Count == 0 ? ";" : $" AND itemid NOT IN ({string.Join(", ", itemIds)});");
            sb.AppendLine();

            // (b) SKUs: rebuilt for the whole sale, the retailer can change an item's list between runs.
            sb.Append("DELETE FROM catalogue_special_sku WHERE shoptype = ").Append(shop).Append(" AND saleid = ").Append(sale).AppendLine(";");
            var skuRows = items
                .SelectMany(i => i.Skus.Select(s => (i.Item.ItemId, Sku: s)))
                .Where(r => !string.IsNullOrWhiteSpace(r.Sku))
                .Distinct()
                .ToList();
            foreach (var chunk in skuRows.Chunk(RowsPerInsert))
            {
                sb.AppendLine("INSERT INTO catalogue_special_sku (shoptype, saleid, itemid, sku) VALUES");
                for (var i = 0; i < chunk.Length; i++)
                {
                    sb.Append("    (").Append(shop).Append(", ").Append(sale).Append(", ")
                      .Append(SqlLiteral.Text(chunk[i].ItemId)).Append(", ")
                      .Append(SqlLiteral.Text(chunk[i].Sku.Trim())).Append(')')
                      .AppendLine(i == chunk.Length - 1 ? string.Empty : ",");
                }

                sb.AppendLine("ON CONFLICT DO NOTHING;");
            }

            sb.AppendLine();

            // (c) matching. Several products can share a code (historical duplicates): take the most recently seen.
            if (shopType == 1)
            {
                sb.AppendLine("-- Woolworths: sku = product.stockcode");
                sb.AppendLine("UPDATE catalogue_special_sku k");
                sb.AppendLine("SET productid = p.productid");
                sb.AppendLine("FROM (");
                sb.AppendLine("    SELECT DISTINCT ON (stockcode) stockcode, productid");
                sb.AppendLine("    FROM product");
                sb.AppendLine("    WHERE shoptype = 1 AND stockcode IS NOT NULL");
                sb.AppendLine("    ORDER BY stockcode, lastseenat DESC");
                sb.AppendLine(") p");
                sb.Append("WHERE k.shoptype = 1 AND k.saleid = ").Append(sale).AppendLine(" AND k.sku = p.stockcode;");
            }
            else
            {
                sb.AppendLine("-- Coles: sku = product.sourceid (the Coles product id)");
                sb.AppendLine("UPDATE catalogue_special_sku k");
                sb.AppendLine("SET productid = p.productid");
                sb.AppendLine("FROM (");
                sb.AppendLine("    SELECT DISTINCT ON (sourceid) sourceid, productid");
                sb.AppendLine("    FROM product");
                sb.AppendLine("    WHERE shoptype = 0 AND sourceid IS NOT NULL");
                sb.AppendLine("    ORDER BY sourceid, lastseenat DESC");
                sb.AppendLine(") p");
                sb.Append("WHERE k.shoptype = 0 AND k.saleid = ").Append(sale).AppendLine(" AND k.sku = p.sourceid;");
            }

            sb.AppendLine();

            // (d) retention
            var cutoff = SqlLiteral.Date(validTo.AddDays(-RetentionDays));
            sb.AppendLine("DELETE FROM catalogue_special_sku k USING catalogue_special c");
            sb.AppendLine("WHERE c.shoptype = k.shoptype AND c.saleid = k.saleid AND c.itemid = k.itemid");
            sb.Append("  AND c.validto < ").Append(cutoff).AppendLine(";");
            sb.Append("DELETE FROM catalogue_special WHERE validto < ").Append(cutoff).AppendLine(";");
            sb.AppendLine();
            sb.AppendLine("COMMIT;");
            return sb.ToString();
        }
    }
}
