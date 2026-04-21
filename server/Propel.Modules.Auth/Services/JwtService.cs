using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace Propel.Modules.Auth.Services;

/// <summary>
/// Generates JWT access tokens and cryptographically secure refresh tokens (US_011, AC-1, AC-3).
/// Access tokens carry <c>sub</c>, <c>role</c>, <c>jti</c>, <c>deviceId</c>, <c>iat</c>, and <c>exp</c>
/// claims and expire in exactly 15 minutes with zero ClockSkew tolerance (NFR-007).
/// Refresh tokens are CSPRNG-derived 64-byte (512-bit) values encoded as Base64 (OWASP A02).
/// The signing key is read from <c>IConfiguration["Jwt:SigningKey"]</c> — never hard-coded.
/// </summary>
public sealed class JwtService : IJwtService
{
    private readonly string _signingKey;
    private readonly string _issuer;
    private readonly string _audience;

    public JwtService(IConfiguration configuration)
    {
        _signingKey = configuration["Jwt:SecretKey"]
            ?? throw new InvalidOperationException("Jwt:SecretKey is not configured.");
        _issuer = configuration["Jwt:Issuer"]
            ?? throw new InvalidOperationException("Jwt:Issuer is not configured.");
        _audience = configuration["Jwt:Audience"]
            ?? throw new InvalidOperationException("Jwt:Audience is not configured.");
    }

    /// <inheritdoc />
    public string GenerateAccessToken(Guid userId, string role, Guid jti, string deviceId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_signingKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Role, role),
            new Claim(JwtRegisteredClaimNames.Jti, jti.ToString()),
            new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64),
            new Claim("deviceId", deviceId)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(900),   // 15 minutes — NFR-007
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc />
    public string GenerateRefreshToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)); // 512-bit entropy, OWASP A02

    /// <inheritdoc />
    public string HashToken(string rawToken)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

/// <summary>
/// Abstraction over JWT and refresh-token generation (enables unit-test mocking).
/// </summary>
public interface IJwtService
{
    /// <summary>
    /// Creates a signed JWT access token with a 900-second (15-min) expiry.
    /// </summary>
    string GenerateAccessToken(Guid userId, string role, Guid jti, string deviceId);

    /// <summary>
    /// Returns a cryptographically random 512-bit (64-byte) refresh token encoded as Base64.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Computes the lowercase SHA-256 hex digest of a raw token value.
    /// Use this before persisting or comparing any token (OWASP A02).
    /// </summary>
    string HashToken(string rawToken);
}
