using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.Compare
{
    public class CategoryKeyword
    {
        [Key]
        public int KeywordId { get; set; }
        public int CategoryId { get; set; }
        public string Keyword { get; set; } = default!;
        public int Weight { get; set; } = 1;
    }
}