namespace Girus.Application.Employees;

public interface IEmployeeService
{
    Task<List<EmployeeDto>> GetAllAsync(CancellationToken ct);
    Task<EmployeeDto> SaveAsync(SaveEmployeeRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);

    /// <summary>Só permitido entre empresas do MESMO Administrator (mesmo dono/grupo). Retorna null se a empresa
    /// de destino pertencer a outro dono.</summary>
    Task<EmployeeDto?> TransferAsync(Guid employeeId, Guid targetCompanyId, CancellationToken ct);
}
