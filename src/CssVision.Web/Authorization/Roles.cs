namespace CssVision.Web.Authorization;

/// <summary>
/// Papéis do sistema. Admin e GestorMaster já podiam existir na aplicação administrativa;
/// GestorComercial e Comercial são os novos papéis do time de vendas (CRM).
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string GestorMaster = "GestorMaster";
    public const string GestorComercial = "GestorComercial";
    public const string Comercial = "Comercial";

    public static readonly string[] All = [Admin, GestorMaster, GestorComercial, Comercial];

    /// <summary>Perfis com acesso à área administrativa (dashboards, importações, auditoria etc.).</summary>
    public static readonly string[] Administrativos = [Admin, GestorMaster];

    /// <summary>Perfis com acesso ao CRM.</summary>
    public static readonly string[] Comerciais = [Admin, GestorMaster, GestorComercial, Comercial];

    /// <summary>Perfis que enxergam toda a base (sem restrição por carteira/equipe).</summary>
    public static readonly string[] VisaoTotal = [Admin, GestorMaster];

    /// <summary>Perfis que gerenciam equipe comercial (distribuição, metas, configurações do funil).</summary>
    public static readonly string[] GestaoComercial = [Admin, GestorMaster, GestorComercial];
}
