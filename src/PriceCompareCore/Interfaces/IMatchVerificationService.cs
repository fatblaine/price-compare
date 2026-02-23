using System.Collections.Generic;
using System.Threading.Tasks;
using PriceCompareData.Entities.Compare;

namespace PriceCompareCore.Interfaces
{
    // LLM validation for low-confidence matches.
    public interface IMatchVerificationService
    {
        Task<string> VerifyAsync(Product source, IReadOnlyList<Product> candidates);
    }
}
