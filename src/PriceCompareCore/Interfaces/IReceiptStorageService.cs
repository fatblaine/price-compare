using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace PriceCompareCore.Interfaces
{
    public interface IReceiptStorageService
    {
        Task<string> UploadAsync(IFormFile file, string userId, int receiptId);
    }
}
