using System;
using System.Threading.Tasks;
using PriceCompareData.DTOs;

namespace PriceCompareCore.Interfaces
{
    public interface IMatchLlmReviewService
    {
        Task<Guid> StartAsync(MatchLlmReviewRequest request);
    }
}
