using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareCore.Interfaces;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Services
{
    public class NullEmbeddingService : IEmbeddingService
    {
        public Task<float[]?> EmbedAsync(Product product)
        {
            return Task.FromResult<float[]?>(null);
        }
    }
}