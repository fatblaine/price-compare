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
    }
}