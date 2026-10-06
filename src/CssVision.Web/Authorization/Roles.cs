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

    /// <summary>Supervisor comercial: vê e faz o mesmo que o administrador, exceto o tráfego pago (AreaMarketing).</summary>
    public const string SupervisorComercial = "SupervisorComercial";

    /// <summary>Acompanhamento de tráfego pago (leads, origem, campanhas) — não tem acesso ao resto do CRM.</summary>
    public const string Marketing = "Marketing";

    /// <summary>
    /// Financeiro: atribuído a uma regional, vê só os dados dela. Acompanha a gestão comercial (leads por consultor, quem recebe lead),
    /// ativa/inativa contas de consultores e avisa os consultores de pagamentos em aberto — não acessa leads, pipeline nem o resto do CRM.
    /// </summary>
    public const string Financeiro = "Financeiro";

    public static readonly string[] All = [Admin, GestorMaster, GestorComercial, Comercial, Marketing, SupervisorComercial, Financeiro];

    /// <summary>Perfis com acesso à página de tráfego pago.</summary>
    public static readonly string[] AreaMarketing = [Admin, Marketing];

    /// <summary>Perfis com acesso à área administrativa (dashboards, importações, auditoria etc.).</summary>
    public static readonly string[] Administrativos = [Admin, GestorMaster, SupervisorComercial];

    /// <summary>Perfis com acesso ao CRM.</summary>
    public static readonly string[] Comerciais = [Admin, GestorMaster, SupervisorComercial, GestorComercial, Comercial];

    /// <summary>Perfis que enxergam toda a base (sem restrição por carteira/equipe).</summary>
    public static readonly string[] VisaoTotal = [Admin, GestorMaster, SupervisorComercial];

    /// <summary>Perfis que gerenciam equipe comercial (distribuição, metas, configurações do funil).</summary>
    public static readonly string[] GestaoComercial = [Admin, GestorMaster, SupervisorComercial, GestorComercial];

    /// <summary>Perfis que acompanham a carteira dos consultores, ligam/desligam a chegada de lead e avisam de pagamentos em aberto.</summary>
    public static readonly string[] GestaoFinanceira = [Admin, GestorMaster, SupervisorComercial, GestorComercial, Financeiro];
}
