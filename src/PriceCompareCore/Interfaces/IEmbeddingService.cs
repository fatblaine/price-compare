using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Interfaces
{
    public interface IEmbeddingService
    {
        Task<float[]?> EmbedAsync(Product product);
    }
}