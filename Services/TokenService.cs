using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using BarberPro.Dominio;

namespace BarberPro.Services;

public class TokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string GenerateAccessToken(Usuario usuario)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new Claim(ClaimTypes.Name, usuario.Nombre),
            new Claim(ClaimTypes.Email, usuario.Email),
            new Claim(ClaimTypes.Role, usuario.Rol),
            new Claim("Estado", usuario.Estado)
        };

        if (usuario.BarberoId.HasValue)
            claims.Add(new Claim("BarberoId", usuario.BarberoId.Value.ToString()));

        var expiresAt = DateTime.UtcNow.AddMinutes(
            Convert.ToDouble(_configuration["Jwt:ExpiresInMinutes"] ?? "60"));

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string GenerateRefreshToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray());
    }

    public DateTime GetAccessTokenExpiration()
    {
        return DateTime.UtcNow.AddMinutes(
            Convert.ToDouble(_configuration["Jwt:ExpiresInMinutes"] ?? "60"));
    }

    public DateTime GetRefreshTokenExpiration()
    {
        return DateTime.UtcNow.AddDays(
            Convert.ToDouble(_configuration["Jwt:RefreshExpiresInDays"] ?? "7"));
    }

    public bool IsRefreshTokenValid(Usuario usuario)
    {
        return !string.IsNullOrEmpty(usuario.RefreshToken)
            && usuario.RefreshTokenExpiry.HasValue
            && usuario.RefreshTokenExpiry.Value > DateTime.UtcNow;
    }

    public bool ValidateRefreshToken(Usuario usuario, string refreshToken)
    {
        return IsRefreshTokenValid(usuario)
            && usuario.RefreshToken == refreshToken;
    }
}
