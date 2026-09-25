namespace Girus.Application.Abstractions;

/// <summary>Dados do usuário autenticado, extraídos das claims do JWT da requisição atual.</summary>
public interface ICurrentUserContext
{
    bool IsAuthenticated { get; }

    /// <summary>Id do usuário: "usr-{administratorId}" para donos, ou o Id do Employee para funcionários.</summary>
    Guid UserId { get; }
    string UserName { get; }
    string UserEmail { get; }

    /// <summary>"admin" | "gerente" | "operador".</summary>
    string Role { get; }

    Guid CompanyId { get; }

    /// <summary>Preenchido apenas quando o usuário autenticado é um Administrator (dono).</summary>
    Guid? AdministratorId { get; }

    /// <summary>Preenchido apenas quando o usuário autenticado é um Employee.</summary>
    Guid? EmployeeId { get; }

    /// <summary>true quando o usuário é o dono/administrador (não um funcionário) — controla troca/cadastro de empresas.</summary>
    bool IsOwner { get; }
}
