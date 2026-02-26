using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PriceCompareData.DTOs;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Interfaces
{
    public interface IMatchJobService
    {
        Task<Guid> StartAsync(MatchRunRequest request);
        Task<MatchJob> GetAsync(Guid jobId);

        // Search match jobs with filters + paging.
        Task<MatchJobListResponse> SearchAsync(MatchJobQueryRequest request);
    }
}