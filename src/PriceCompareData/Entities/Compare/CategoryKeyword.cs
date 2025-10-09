using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareData.Entities.Compare
{
    [Table("CategoryKeyword")]
    public class CategoryKeyword
    {
        [Key]
        [Column("keywordid")]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int KeywordId { get; set; }
        public int CategoryId { get; set; }
        public string Keyword { get; set; } = default!;
        public int Weight { get; set; } = 1;
    }
}