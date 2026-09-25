namespace Girus.Application.Abstractions;

public record JwtIssueRequest(
    Guid UserId,
    string UserName,
    string UserEmail,
    string Role,
    Guid CompanyId,
    Guid? AdministratorId,
    Guid? EmployeeId);

public interface IJwtTokenService
{
    /// <returns>O token assinado e o instante de expiração (UTC).</returns>
    (string Token, DateTime ExpiresAtUtc) IssueToken(JwtIssueRequest request);
}
