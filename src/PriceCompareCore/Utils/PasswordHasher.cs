using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;

namespace PriceCompareCore.Utils
{
    public class PasswordHasher
    {
        private readonly IPasswordHasher<object> _inner = new PasswordHasher<object>();
        private static readonly object _dummy = new object();

        public string HashPassword(string password)
        {
            return _inner.HashPassword(_dummy, password);
        }

        public bool VerifyPassword(string password, string storedHash)
        {
            var result = _inner.VerifyHashedPassword(_dummy, storedHash, password);
            return result != PasswordVerificationResult.Failed;
        }
    }
}