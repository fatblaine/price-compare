using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PriceCompareCore.Config;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Utils;
using PriceCompareData.Data;
using PriceCompareData.Entities.Users;

namespace PriceCompareCore.Services
{
    public class AuthService : IAuthService
    {
        private readonly AppDbContext _db;
        private readonly PasswordHasher _hasher;
        private readonly JwtSettings _jwt;

        public AuthService(
            AppDbContext db,
            PasswordHasher hasher,
            IOptions<JwtSettings> jwtOptions)
        {
            _db = db;
            _hasher = hasher;
            _jwt = jwtOptions.Value;
        }

        public async Task<string> Login(string email, string password)
        {
            List<User> list = await _db.Users.ToListAsync();
            User found = null;

            foreach (User u in list)
            {
                if (u.Email == email && _hasher.VerifyPassword(password, u.PasswordHash))
                {
                    found = u;
                    break;
                }
            }

            if (found == null)
            {
                throw new Exception("Invalid email or password");
            }

            return GenerateJwt(found);
        }

        public async Task<string> Register(string email, string password)
        {
            List<User> list = await _db.Users.ToListAsync();
            foreach (User u in list)
            {
                if (u.Email == email)
                {
                    throw new Exception("Email already exists");
                }
            }

            User newUser = new User();
            newUser.Id = Guid.NewGuid();
            newUser.Email = email;
            newUser.PasswordHash = _hasher.HashPassword(password);
            newUser.CreatedAt = DateTime.UtcNow;

            _db.Users.Add(newUser);
            await _db.SaveChangesAsync();
            return newUser.Id.ToString();
        }

        private string GenerateJwt(User user)
        {
            JwtSecurityTokenHandler handler = new JwtSecurityTokenHandler();
            byte[] key = Encoding.ASCII.GetBytes(_jwt.Secret);

            SecurityTokenDescriptor descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Email, user.Email)
                }),
                Expires = DateTime.UtcNow.AddMinutes(_jwt.ExpireMinutes),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            SecurityToken token = handler.CreateToken(descriptor);
            return handler.WriteToken(token);
        }
    }
}
