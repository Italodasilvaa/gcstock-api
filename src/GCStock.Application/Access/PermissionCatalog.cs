namespace GCStock.Application.Access;

/// <summary>Permissão configurável por funcionário. <paramref name="Group"/> agrupa por tela/módulo no cadastro de funcionários.</summary>
public record PermissionInfo(string Key, string Label, string Description, string Group);

/// <summary>
/// Permissões granulares das operações de escrita (criar/editar/excluir) de cada tela, atribuídas a funcionários.
/// O dono da empresa (Administrator) possui todas implicitamente, sem cadastro. Consultar (GET) é liberado a qualquer
/// usuário autenticado. Para criar uma nova permissão: adicione a constante e a entrada em <see cref="All"/> e proteja
/// a rota com <c>.RequireAuthorization(Permissions.Xxx)</c>; a policy é registrada sozinha e o front exibe a opção.
/// </summary>
public static class Permissions
{
    // Produtos
    public const string ProductsCreate = "produtos.criar";
    public const string ProductsEdit = "produtos.editar";
    public const string ProductsDelete = "produtos.excluir";

    // Categorias de produto
    public const string CategoriesCreate = "categorias.criar";
    public const string CategoriesEdit = "categorias.editar";
    public const string CategoriesDelete = "categorias.excluir";

    // Kits de churrasco
    public const string KitsCreate = "kits.criar";
    public const string KitsEdit = "kits.editar";
    public const string KitsDelete = "kits.excluir";

    // Compras / Entradas
    public const string PurchasesCreate = "compras.criar";
    public const string PurchasesEdit = "compras.editar";
    public const string PurchasesDelete = "compras.excluir";

    // Vendas (PDV)
    public const string SalesCreate = "vendas.criar";
    /// <summary>Vender com preço diferente do cadastro (validado no PosService, não por rota).</summary>
    public const string SalesEditPrice = "vendas.alterar-preco";
    /// <summary>Cancelar (estornar) uma venda já finalizada.</summary>
    public const string SalesCancel = "vendas.cancelar";

    // Clientes
    public const string ClientsCreate = "clientes.criar";
    public const string ClientsEdit = "clientes.editar";
    public const string ClientsDelete = "clientes.excluir";

    // Funcionários (a atribuição de permissões é exclusiva do dono)
    public const string EmployeesCreate = "funcionarios.criar";
    public const string EmployeesEdit = "funcionarios.editar";
    public const string EmployeesDelete = "funcionarios.excluir";

    // Perdas de estoque
    public const string LossesCreate = "perdas.registrar";
    public const string LossesUndo = "perdas.desfazer";

    // Caixa da empresa
    public const string CapitalManage = "aportes.gerenciar";

    // Financeiro
    public const string PayablesCreate = "contas-pagar.criar";
    public const string PayablesSettle = "contas-pagar.baixar";
    public const string PayablesDelete = "contas-pagar.excluir";
    public const string PayablesEdit = "contas-pagar.editar";
    public const string ReceivablesEdit = "contas-receber.editar";
    public const string ReceivablesDelete = "contas-receber.excluir";
    public const string ReceivablesCreate = "contas-receber.criar";
    public const string ReceivablesSettle = "contas-receber.liquidar";
    public const string CardsAnticipate = "cartoes.antecipar";

    // Configurações
    public const string FeesCreate = "taxas.criar";
    public const string FeesEdit = "taxas.editar";
    public const string FeesDelete = "taxas.excluir";
    public const string AlertsEdit = "alertas.editar";

    public static readonly IReadOnlyList<PermissionInfo> All =
    [
        new(ProductsCreate, "Cadastrar produtos", "Criar novos produtos no estoque.", "Produtos"),
        new(ProductsEdit, "Editar produtos", "Alterar dados, preços e estoque dos produtos.", "Produtos"),
        new(ProductsDelete, "Excluir produtos", "Remover produtos do estoque.", "Produtos"),

        new(CategoriesCreate, "Criar categorias", "Criar categorias de produto.", "Categorias de produto"),
        new(CategoriesEdit, "Editar categorias", "Renomear e reordenar categorias de produto.", "Categorias de produto"),
        new(CategoriesDelete, "Excluir categorias", "Remover categorias de produto.", "Categorias de produto"),

        new(KitsCreate, "Criar kits de churrasco", "Montar novos kits de churrasco.", "Kits de churrasco"),
        new(KitsEdit, "Editar kits de churrasco", "Alterar componentes, preço e imagem dos kits.", "Kits de churrasco"),
        new(KitsDelete, "Excluir kits de churrasco", "Remover kits de churrasco.", "Kits de churrasco"),

        new(PurchasesCreate, "Lançar compras", "Registrar novas entradas de compra.", "Compras"),
        new(PurchasesEdit, "Editar pedidos de compra", "Alterar entradas de compra já lançadas, inclusive as já pagas.", "Compras"),
        new(PurchasesDelete, "Excluir pedidos de compra", "Apagar uma entrada de compra lançada por engano (estorna estoque e custo; só se nada dela foi vendido).", "Compras"),

        new(SalesCreate, "Realizar vendas", "Finalizar vendas no PDV.", "Vendas (PDV)"),
        new(SalesCancel, "Cancelar vendas", "Cancelar (estornar) vendas já finalizadas: devolve o estoque e desfaz o título a receber.", "Vendas (PDV)"),
        new(SalesEditPrice, "Alterar preço na venda", "Mudar o preço de um item no carrinho do PDV, vendendo por valor diferente do cadastro.", "Vendas (PDV)"),

        new(ClientsCreate, "Cadastrar clientes", "Criar novos clientes.", "Clientes"),
        new(ClientsEdit, "Editar clientes", "Alterar dados e limite de crédito dos clientes.", "Clientes"),
        new(ClientsDelete, "Excluir clientes", "Apagar clientes sem contas a receber em aberto.", "Clientes"),

        new(EmployeesCreate, "Cadastrar funcionários", "Criar o acesso de novos funcionários.", "Funcionários"),
        new(EmployeesEdit, "Editar funcionários", "Alterar dados, senha e acesso de funcionários.", "Funcionários"),
        new(EmployeesDelete, "Excluir funcionários", "Remover o acesso de funcionários.", "Funcionários"),

        new(LossesCreate, "Registrar perdas", "Lançar perdas de estoque (vencimento, quebra, furto...) com o valor do prejuízo; abate do estoque.", "Perdas e quebras"),
        new(LossesUndo, "Desfazer perdas", "Desfazer uma perda lançada por engano; devolve a quantidade ao estoque.", "Perdas e quebras"),

        new(CapitalManage, "Lançar aportes no caixa", "Registrar e excluir aportes de capital (dinheiro colocado na empresa).", "Caixa da empresa"),

        new(PayablesCreate, "Lançar contas a pagar", "Criar títulos de contas a pagar.", "Contas a pagar"),
        new(PayablesSettle, "Dar baixa em contas a pagar", "Marcar contas a pagar como pagas.", "Contas a pagar"),
        new(PayablesEdit, "Editar contas a pagar", "Corrigir descrição, valor, vencimento e categoria de despesas lançadas.", "Contas a pagar"),
        new(PayablesDelete, "Excluir despesas lançadas", "Apagar uma despesa ou equipamento lançado por engano (sai do caixa e do resultado).", "Contas a pagar"),

        new(ReceivablesCreate, "Lançar contas a receber", "Criar títulos de contas a receber.", "Contas a receber"),
        new(ReceivablesEdit, "Editar contas a receber", "Corrigir título a receber em aberto (vendas só mudam o vencimento).", "Contas a receber"),
        new(ReceivablesDelete, "Excluir contas a receber", "Apagar título a receber manual em aberto.", "Contas a receber"),
        new(ReceivablesSettle, "Liquidar contas a receber", "Marcar contas a receber como recebidas.", "Contas a receber"),

        new(CardsAnticipate, "Antecipar recebíveis de cartão", "Antecipar liquidações de cartão.", "Cartões"),

        new(FeesCreate, "Criar taxas de pagamento", "Cadastrar formas de pagamento e suas taxas.", "Configurações"),
        new(FeesEdit, "Editar taxas de pagamento", "Alterar, ativar/desativar e reordenar taxas.", "Configurações"),
        new(FeesDelete, "Excluir taxas de pagamento", "Remover formas de pagamento cadastradas.", "Configurações"),
        new(AlertsEdit, "Editar alertas sanitários", "Alterar a configuração dos alertas de validade.", "Configurações"),
    ];

    public static IReadOnlyList<string> AllKeys { get; } = All.Select(p => p.Key).ToList();

    public static bool IsValid(string? key) => key is not null && AllKeys.Contains(key);
}
