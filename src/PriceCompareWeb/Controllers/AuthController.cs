using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PriceCompareCore.Exceptions;
using PriceCompareCore.Interfaces;

namespace PriceCompareWeb.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(IAuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register([FromBody] AuthRequest request)
        {
            try
            {
                await _authService.Register(request.Email, request.Password);
                return Ok(new { message = "Verification code sent. Please check your email." });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Register failed for {Email}", request.Email);
                return BadRequest(new { error = "register_failed", message = "Registration failed. Please try again." });
            }
        }

        [HttpPost("verify-email")]
        [AllowAnonymous]
        public async Task<IActionResult> VerifyEmail([FromBody] VerifyEmailRequest request)
        {
            try
            {
                var token = await _authService.VerifyEmail(request.Email, request.Otp);
                return Ok(new { token });
            }
            catch (OtpExpiredException ex)
            {
                _logger.LogInformation("OTP expired for {Email}", request.Email);
                return BadRequest(new { error = "invalid_or_expired", message = ex.Message });
            }
            catch (OtpMismatchException ex)
            {
                _logger.LogInformation("OTP mismatch for {Email}", request.Email);
                return BadRequest(new { error = "invalid_otp", message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VerifyEmail failed for {Email}", request.Email);
                return BadRequest(new { error = "verify_failed", message = "Verification failed. Please try again." });
            }
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<IActionResult> Login([FromBody] AuthRequest request)
        {
            try
            {
                var token = await _authService.Login(request.Email, request.Password);
                return Ok(new { token });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Login failed for {Email}", request.Email);
                return Unauthorized("Invalid credentials");
            }
        }
    }

    public class AuthRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string Password { get; set; } = string.Empty;
    }

    public class VerifyEmailRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, StringLength(6, MinimumLength = 6)]
        public string Otp { get; set; } = string.Empty;
    }
}
