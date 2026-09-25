namespace Girus.Application.Employees;

public record EmployeeDto(
    string Id,
    string CompanyId,
    string Name,
    string Email,
    string Role,
    bool Active,
    DateOnly CreatedAt,
    decimal Salary);

public record SaveEmployeeRequest(
    string? Id,
    string Name,
    string Email,
    string? Password,
    string? Role,
    bool? Active,
    decimal? Salary);
