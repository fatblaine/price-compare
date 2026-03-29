using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using PriceCompareData.Data;
using PriceCompareData.Entities.Users;

namespace PriceCompareWeb.Filters
{
    /// <summary>
    /// On every authenticated request, auto-creates a Users row for the Cognito user
    /// (identified by the JWT 'sub' claim) if one does not already exist.
    /// This replaces the old self-service register/verify flow.
    /// </summary>
    public class CognitoUserProvisionFilter : IAsyncActionFilter
    {
        private readonly AppDbContext _db;

        public CognitoUserProvisionFilter(AppDbContext db)
        {
            _db = db;
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var principal = context.HttpContext.User;

            if (principal?.Identity?.IsAuthenticated == true)
            {
                // With MapInboundClaims = true, JWT 'sub' -> ClaimTypes.NameIdentifier
                var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier);
                var email = principal.FindFirstValue(ClaimTypes.Email) ?? "";

                if (!string.IsNullOrEmpty(sub) && Guid.TryParse(sub, out var userId))
                {
                    bool exists = await _db.Users.AnyAsync(u => u.Id == userId);
                    if (!exists)
                    {
                        try
                        {
                            _db.Users.Add(new User
                            {
                                Id = userId,
                                Email = email,
                                PasswordHash = "", // Cognito owns credentials — not stored here
                                CreatedAt = DateTime.UtcNow
                            });
                            await _db.SaveChangesAsync();
                        }
                        catch (DbUpdateException)
                        {
                            // Concurrent request already inserted this user — safe to ignore
                            _db.ChangeTracker.Clear();
                        }
                    }
                }
            }

            await next();
        }
    }
}
