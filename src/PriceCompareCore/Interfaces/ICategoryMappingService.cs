using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareCore.Interfaces
{
    public interface ICategoryMappingService
    {
        // Parse size and package quantity from product name
        (decimal? sizeValue, string? sizeUnit, int? pkgQty) ParseSpec(string name);

        // Try to map categoryId by name and brand, return null if not found
        int? MapCategoryId(string name, string? brand = null);
    }
}