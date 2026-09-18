namespace Girus.Domain.Entities;

/// <summary>
/// Dono/administrador do grupo. Um Administrator pode ter N empresas (Company.OwnerId);
/// toda empresa precisa de exatamente um dono.
/// </summary>
public class Administrator
{
    public string Id { get; set; } = "admin_" + Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Company> Companies { get; set; } = [];
}

/// <summary>Plano contratado: "free_trial" | "pro" | "enterprise".</summary>
public class Company
{
    public string Id { get; set; } = "comp_" + Guid.NewGuid().ToString("N");
    public string CorporateName { get; set; } = string.Empty;
    public string TradeName { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public Administrator? Owner { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Active { get; set; } = true;
    public string Plan { get; set; } = "free_trial";
}

/// <summary>Role: "admin" | "gerente" | "operador". Cadastrado pelo administrador da empresa.</summary>
public class Employee
{
    public string Id { get; set; } = "emp_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "operador";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Type: "matriz" | "filial" | "cd".</summary>
public class Branch
{
    public string Id { get; set; } = "branch_" + Guid.NewGuid().ToString("N");
    public string CompanyId { get; set; } = string.Empty;
    public Company? Company { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Cnpj { get; set; } = string.Empty;
    public int OperatorsCount { get; set; }
    public bool Active { get; set; } = true;
    public string Type { get; set; } = "matriz";
}
