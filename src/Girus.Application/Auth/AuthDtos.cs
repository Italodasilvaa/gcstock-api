namespace Girus.Application.Auth;

public record CompanySummaryDto(
    string Id,
    string CorporateName,
    string TradeName,
    string Document,
    string OwnerId,
    string OwnerName,
    string Phone,
    string Segment,
    string City,
    string State,
    DateOnly CreatedAt,
    bool Active,
    int BranchesCount,
    string Plan);

public record AuthUserDto(string Id, string Name, string Email, string Role, string? EmployeeId);

public record AuthSessionDto(AuthUserDto User, CompanySummaryDto Company, string Token, DateTime LoggedInAt);

public record LoginRequest(string Email, string? Password);

public record RegisterEmployeeRow(string Name, string Email, string? Password, string Role);

public record RegisterCompanyRequest(
    string CorporateName,
    string TradeName,
    string Document,
    string OwnerName,
    string Email,
    string? Password,
    string Phone,
    string Segment,
    string City,
    string State,
    List<RegisterEmployeeRow>? Employees);
