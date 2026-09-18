namespace Girus.Infra.Security;

/// <summary>Nomes de claims customizadas usadas no JWT do Girus (além das claims padrão sub/name/email/role).</summary>
public static class ClaimTypesGirus
{
    public const string CompanyId = "company_id";
    public const string AdministratorId = "administrator_id";
    public const string EmployeeId = "employee_id";
}
