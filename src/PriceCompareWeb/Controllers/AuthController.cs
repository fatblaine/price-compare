using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
                var userId = await _authService.Register(request.Email, request.Password);
                return Ok(new { userId });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Register failed for {Email}", request.Email);
                return BadRequest(ex.Message);
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
                return Unauthorized(ex.Message);
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
}
