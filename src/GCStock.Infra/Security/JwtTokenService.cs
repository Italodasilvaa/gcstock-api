using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GCStock.Application.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace GCStock.Infra.Security;

public class JwtTokenService(IOptions<JwtSettings> options) : IJwtTokenService
{
    private readonly JwtSettings _settings = options.Value;

    public (string Token, DateTime ExpiresAtUtc) IssueToken(JwtIssueRequest request)
    {
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(_settings.ExpiresMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, request.UserId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(ClaimTypes.Name, request.UserName),
            new(ClaimTypes.Email, request.UserEmail),
            new(ClaimTypes.Role, request.Role),
            new(ClaimTypesGCStock.CompanyId, request.CompanyId.ToString()),
        };

        if (request.AdministratorId.HasValue)
        {
            claims.Add(new Claim(ClaimTypesGCStock.AdministratorId, request.AdministratorId.Value.ToString()));
        }

        if (request.EmployeeId.HasValue)
        {
            claims.Add(new Claim(ClaimTypesGCStock.EmployeeId, request.EmployeeId.Value.ToString()));
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
