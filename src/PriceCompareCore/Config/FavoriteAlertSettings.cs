using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareCore.Config
{
    public class FavoriteAlertSettings
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string FavoritesPath { get; set; } = "/";

        /// <summary>
        /// Minimum week-over-week drop percentage required to trigger an alert.
        /// 0 (default) = notify on any drop. Set e.g. 2 to suppress trivial dips.
        /// </summary>
        public decimal MinDropPercent { get; set; } = 0m;
    }
}