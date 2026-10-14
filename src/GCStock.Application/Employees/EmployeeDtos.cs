namespace GCStock.Application.Employees;

public record EmployeeDto(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Email,
    string Role,
    bool Active,
    DateOnly CreatedAt,
    decimal Salary);

public record SaveEmployeeRequest(
    Guid? Id,
    string Name,
    string Email,
    string? Password,
    string? Role,
    bool? Active,
    decimal? Salary);
