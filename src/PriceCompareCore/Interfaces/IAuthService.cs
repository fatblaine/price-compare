using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareCore.Interfaces
{
    public interface IAuthService
    {
        Task<string> Register(string email, string password);
        Task<string> Login(string email, string password);
    }
}