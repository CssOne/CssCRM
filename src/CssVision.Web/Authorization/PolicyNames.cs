namespace CssVision.Web.Authorization;

public static class PolicyNames
{
    /// <summary>Área administrativa (dashboards, importações, auditoria, backups, configurações).</summary>
    public const string AreaAdministrativa = "AreaAdministrativa";

    /// <summary>Área comercial / CRM (leads, pipeline, atividades, agenda, metas).</summary>
    public const string AreaComercial = "AreaComercial";

    /// <summary>Gestão comercial: distribuição de leads, metas, configurações do funil.</summary>
    public const string GestaoComercial = "GestaoComercial";

    /// <summary>Visão consolidada / total, sem restrição de carteira ou equipe.</summary>
    public const string VisaoTotalComercial = "VisaoTotalComercial";
}
