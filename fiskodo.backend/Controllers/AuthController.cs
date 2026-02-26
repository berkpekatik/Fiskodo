using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Fiskodo.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public sealed record AuthRequest(string Username, string Password);

    public sealed record AuthResponse(string Token, DateTime ExpiresAt);

    [HttpPost("token")]
    public ActionResult<AuthResponse> CreateToken([FromBody] AuthRequest request)
    {
        var authSection = _configuration.GetSection("Auth");
        var expectedUsername = authSection["Username"];
        var expectedPassword = authSection["Password"];

        if (!string.Equals(request.Username, expectedUsername, StringComparison.Ordinal) ||
            !string.Equals(request.Password, expectedPassword, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        var jwtSection = _configuration.GetSection("Jwt");
        var issuer = jwtSection["Issuer"] ?? "Fiskodo";
        var audience = jwtSection["Audience"] ?? "Fiskodo.Api";
        var secret = jwtSection["Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured.");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expires = DateTime.UtcNow.AddHours(4);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.Username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expires,
            signingCredentials: creds);

        var encodedToken = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new AuthResponse(encodedToken, expires));
    }
}

