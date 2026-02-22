using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Interfaces
{
    // Provides candidate matching for a single source product.
    public interface IProductMatchingService
    {
        Task<IReadOnlyList<ProductMatchCandidate>> MatchCandidatesAsync(Guid sourceProductId, int topN);
    }
}