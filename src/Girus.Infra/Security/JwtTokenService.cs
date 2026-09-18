using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Girus.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Girus.Infra.Security;

public class JwtTokenService(IOptions<JwtSettings> options) : IJwtTokenService
{
    private readonly JwtSettings _settings = options.Value;

    public (string Token, DateTime ExpiresAtUtc) IssueToken(JwtIssueRequest request)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_settings.ExpiresMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, request.UserName),
            new(ClaimTypes.Email, request.UserEmail),
            new(ClaimTypes.Role, request.Role),
            new(ClaimTypesGirus.CompanyId, request.CompanyId),
        };

        if (!string.IsNullOrEmpty(request.AdministratorId))
        {
            claims.Add(new Claim(ClaimTypesGirus.AdministratorId, request.AdministratorId));
        }

        if (!string.IsNullOrEmpty(request.EmployeeId))
        {
            claims.Add(new Claim(ClaimTypesGirus.EmployeeId, request.EmployeeId));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_settings.SecretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _settings.Issuer,
            audience: _settings.Audience,
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAtUtc);
    }
}
