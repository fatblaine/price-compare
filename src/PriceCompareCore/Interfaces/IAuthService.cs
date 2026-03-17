using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PriceCompareCore.Interfaces
{
    public interface IAuthService
    {
        /// <summary>Generates OTP, caches pending registration, sends verification email.</summary>
        Task Register(string email, string password);

        /// <summary>Verifies OTP, creates user in DB, returns JWT.</summary>
        Task<string> VerifyEmail(string email, string otp);

        Task<string> Login(string email, string password);
    }
}