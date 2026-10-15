namespace GCStock.Domain.Entities;

/// <summary>
/// Dono/administrador do grupo. Um Administrator pode ter N empresas (Company.OwnerId);
/// toda empresa precisa de exatamente um dono.
/// </summary>
public class Administrator
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Company> Companies { get; set; } = [];
}

/// <summary>Plano contratado: "free_trial" | "pro" | "enterprise".</summary>
public class Company
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public string CorporateName { get; set; } = string.Empty;
    public string TradeName { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public Guid OwnerId { get; set; }
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
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "operador";
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Salário mensal — somado automaticamente ao fechamento mensal (Financeiro &amp; DRE &gt; Visão Mensal).</summary>
    public decimal Salary { get; set; }

    /// <summary>Permissões granulares (chaves de GCStock.Application.Access.Permissions), ex.: "compras.editar". O dono da empresa não precisa: tem todas.</summary>
    public List<string> Permissions { get; set; } = [];
}

/// <summary>Type: "matriz" | "filial" | "cd".</summary>
public class Branch
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid CompanyId { get; set; }
    public Company? Company { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Cnpj { get; set; } = string.Empty;
    public int OperatorsCount { get; set; }
    public bool Active { get; set; } = true;
    public string Type { get; set; } = "matriz";
}
