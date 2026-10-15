namespace GCStock.Application.Employees;

public record EmployeeDto(
    Guid Id,
    Guid CompanyId,
    string Name,
    string Email,
    string Role,
    bool Active,
    DateOnly CreatedAt,
    decimal Salary,
    List<string> Permissions);

public record SaveEmployeeRequest(
    Guid? Id,
    string Name,
    string Email,
    string? Password,
    string? Role,
    bool? Active,
    decimal? Salary,
    // null = não altera. Só o dono da empresa pode definir (lista vazia remove todas).
    List<string>? Permissions = null);
