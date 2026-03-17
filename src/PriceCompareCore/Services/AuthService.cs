using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PriceCompareCore.Config;
using PriceCompareCore.Exceptions;
using PriceCompareCore.Interfaces;
using PriceCompareCore.Utils;
using PriceCompareData.Data;
using PriceCompareData.Entities.Users;

namespace PriceCompareCore.Services
{
    internal sealed record PendingRegistration(string HashedPassword, string Otp);

    public class AuthService : IAuthService
    {
        private readonly AppDbContext _db;
        private readonly PasswordHasher _hasher;
        private readonly JwtSettings _jwt;
        private readonly IMemoryCache _cache;
        private readonly IEmailSender _emailSender;

        private static readonly TimeSpan OtpTtl = TimeSpan.FromMinutes(15);
        private const string CacheKeyPrefix = "pending_reg:";

        public AuthService(
            AppDbContext db,
            PasswordHasher hasher,
            IOptions<JwtSettings> jwtOptions,
            IMemoryCache cache,
            IEmailSender emailSender)
        {
            _db = db;
            _hasher = hasher;
            _jwt = jwtOptions.Value;
            _cache = cache;
            _emailSender = emailSender;
        }

        public async Task<string> Login(string email, string password)
        {
            User? found = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

            if (found == null || !_hasher.VerifyPassword(password, found.PasswordHash))
            {
                throw new Exception("Invalid email or password");
            }

            return GenerateJwt(found);
        }

        public async Task Register(string email, string password)
        {
            // Hash password before caching — never store plain text
            string hashedPassword = _hasher.HashPassword(password);

            // Generate cryptographically secure 6-digit OTP
            string otp = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

            // Overwrite any existing pending entry (supports resend)
            _cache.Set(
                CacheKeyPrefix + email.ToLowerInvariant(),
                new PendingRegistration(hashedPassword, otp),
                OtpTtl);

            // Send OTP email — do this regardless of whether email is already registered
            // to avoid leaking whether an address has an account
            await _emailSender.SendAsync(
                email,
                "Your PriceCompare verification code",
                $"<p>Your verification code is: <strong style=\"font-size:24px;letter-spacing:4px\">{otp}</strong></p>" +
                $"<p>This code expires in 15 minutes.</p>" +
                $"<p>If you did not request this, you can safely ignore this email.</p>");
        }

        public async Task<string> VerifyEmail(string email, string otp)
        {
            string cacheKey = CacheKeyPrefix + email.ToLowerInvariant();

            if (!_cache.TryGetValue(cacheKey, out PendingRegistration? pending) || pending is null)
            {
                throw new OtpExpiredException("Code is invalid or has expired. Please register again.");
            }

            if (pending.Otp != otp)
            {
                throw new OtpMismatchException("Incorrect verification code.");
            }

            // OTP valid — create the user
            bool exists = await _db.Users.AnyAsync(u => u.Email == email);
            if (exists)
            {
                // Edge case: user completed verification twice (e.g. double-submit)
                // Just log them in
                User existing = await _db.Users.FirstAsync(u => u.Email == email);
                _cache.Remove(cacheKey);
                return GenerateJwt(existing);
            }

            User newUser = new User
            {
                Id = Guid.NewGuid(),
                Email = email,
                PasswordHash = pending.HashedPassword,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(newUser);
            await _db.SaveChangesAsync();

            _cache.Remove(cacheKey);
            return GenerateJwt(newUser);
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
                Issuer = _jwt.Issuer,
                Audience = _jwt.Audience,
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
