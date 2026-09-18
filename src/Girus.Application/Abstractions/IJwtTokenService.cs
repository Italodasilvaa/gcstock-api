namespace Girus.Application.Abstractions;

public record JwtIssueRequest(
    string UserId,
    string UserName,
    string UserEmail,
    string Role,
    string CompanyId,
    string? AdministratorId,
    string? EmployeeId);

public interface IJwtTokenService
{
    /// <returns>O token assinado e o instante de expiração (UTC).</returns>
    (string Token, DateTime ExpiresAtUtc) IssueToken(JwtIssueRequest request);
}
