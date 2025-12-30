using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareCore.Config
{
    public class EmailOptions
    {
        public string Host { get; set; } = string.Empty;
        public int ProductId { get; set; } = 587;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = "PriceCompare";
    }
}