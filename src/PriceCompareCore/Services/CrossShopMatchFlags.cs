using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PriceCompareData.Data;

namespace PriceCompareCore.Services
{
    /// <summary>
    /// Keeps product.hascrossshopmatch in step with productmatch. The flag is true when the product
    /// sits on either side of a same_product match, and the product list sorts on it (see
    /// ProductService) so it never reads productmatch per request.
    ///
    /// Refreshing is best effort: a failure is logged and never fails the caller, because a stale
    /// flag only affects ordering. tools/sql/cross_shop_match_flag.sql runs the same full refresh.
    /// </summary>
    public static class CrossShopMatchFlags
    {
        // Only rows whose value changes are written, so a rerun with no match changes writes nothing.
        private const string SetFlagsSql = @"
UPDATE product SET hascrossshopmatch = true
WHERE NOT hascrossshopmatch
  AND productid IN (
      SELECT sourceproductid FROM productmatch WHERE matchtype = 'same_product'
      UNION
      SELECT targetproductid FROM productmatch WHERE matchtype = 'same_product')";

        private const string ClearFlagsSql = @"
UPDATE product SET hascrossshopmatch = false
WHERE hascrossshopmatch
  AND productid NOT IN (
      SELECT sourceproductid FROM productmatch WHERE matchtype = 'same_product'
      UNION
      SELECT targetproductid FROM productmatch WHERE matchtype = 'same_product')";

        /// <summary>Recomputes the flag for every product. Run after a match job finishes.</summary>
        public static async Task RefreshAllAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
        {
            if (!db.Database.IsRelational())
            {
                return;
            }

            try
            {
                await using var tx = await db.Database.BeginTransactionAsync(ct);
                var set = await db.Database.ExecuteSqlRawAsync(SetFlagsSql, ct);
                var cleared = await db.Database.ExecuteSqlRawAsync(ClearFlagsSql, ct);
                await tx.CommitAsync(ct);
                logger.LogInformation("Cross-shop match flags refreshed set={Set} cleared={Cleared}", set, cleared);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cross-shop match flag refresh failed; product ordering may be stale.");
            }
        }

        /// <summary>Recomputes the flag for the given products only. Run after a manual match edit.</summary>
        public static async Task RefreshProductsAsync(
            AppDbContext db, IEnumerable<Guid> productIds, ILogger logger, CancellationToken ct = default)
        {
            var ids = productIds.Where(id => id != Guid.Empty).Distinct().ToList();
            if (ids.Count == 0 || !db.Database.IsRelational())
            {
                return;
            }

            try
            {
                var flagged = await db.ProductMatches
                    .AsNoTracking()
                    .Where(m => m.MatchType == "same_product"
                                && (ids.Contains(m.SourceProductId) || ids.Contains(m.TargetProductId)))
                    .Select(m => new { m.SourceProductId, m.TargetProductId })
                    .ToListAsync(ct);
                var flaggedSet = flagged
                    .SelectMany(m => new[] { m.SourceProductId, m.TargetProductId })
                    .ToHashSet();

                foreach (var id in ids)
                {
                    var value = flaggedSet.Contains(id);
                    await db.Products
                        .Where(p => p.ProductId == id && p.HasCrossShopMatch != value)
                        .ExecuteUpdateAsync(s => s.SetProperty(p => p.HasCrossShopMatch, value), ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Cross-shop match flag refresh failed for {Count} products.", ids.Count);
            }
        }
    }
}
