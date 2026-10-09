using CssVision.Web.Domain.Crm;
using Microsoft.AspNetCore.Identity;

namespace CssVision.Web.Domain.Identity;

/// <summary>
/// Usuário compartilhado entre a área administrativa e o CRM.
/// Estende o IdentityUser padrão com dados usados pela hierarquia comercial.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    public string NomeCompleto { get; set; } = string.Empty;

    /// <summary>Gestor comercial responsável por este usuário, quando ele for do time de vendas (Comercial).</summary>
    public Guid? GestorComercialId { get; set; }
    public ApplicationUser? GestorComercial { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>Regional à qual este usuário pertence (Comercial/GestorComercial). Nulo para Admin/GestorMaster (visão global).</summary>
    public Guid? RegionalId { get; set; }
    public CrmRegional? Regional { get; set; }

    /// <summary>
    /// Regionais ocultas para este usuário (ids separados por vírgula; ver EscopoRegional). Só vale para quem tem visão total
    /// (Admin/Gestor master/Supervisor): ele deixa de ver os dados dessas regionais. Nulo = vê todas.
    /// </summary>
    public string? RegionaisOcultas { get; set; }

    /// <summary>Subgrupo do consultor dentro da regional (ex: "Externos", "Internos"). Apenas organizacional.</summary>
    public Guid? GrupoId { get; set; }
    public CrmGrupo? Grupo { get; set; }

    /// <summary>Teto de leads que a distribuição automática atribui a este vendedor por mês corrente. Nulo = sem limite.</summary>
    public int? LimiteMensalLeads { get; set; }

    /// <summary>Teto de leads do tráfego pago que a distribuição automática atribui a este vendedor por dia (horário de Brasília). Nulo = sem limite.</summary>
    public int? LimiteDiarioLeads { get; set; }

    /// <summary>Início da faixa de horário (Brasília) em que a distribuição automática entrega leads a este vendedor. Nulo (com o fim) = o dia todo.</summary>
    public TimeOnly? HorarioInicioLeads { get; set; }

    /// <summary>Fim (exclusivo) da faixa de horário de recebimento de leads. Menor que o início = atravessa a meia-noite.</summary>
    public TimeOnly? HorarioFimLeads { get; set; }

    /// <summary>Dias da semana em que recebe leads, em bitmask (1 &lt;&lt; (int)DayOfWeek; domingo = bit 0). Nulo = todos os dias.</summary>
    public int? DiasSemanaLeads { get; set; }

    /// <summary>
    /// Se o vendedor entra no rodízio da distribuição automática. Desligado na Gestão comercial, ele
    /// para de receber leads novos, mas continua entrando no CRM e trabalhando a própria carteira.
    /// </summary>
    public bool RecebeLeads { get; set; } = true;

    /// <summary>
    /// Administrador (visão total) que só administra a plataforma: com <c>false</c> ele não pega leads e o nome dele não aparece em nada que
    /// remete a vendas (Gestão comercial, listas de consultores, quadro, rankings, TV). Os demais papéis são sempre <c>true</c>.
    /// </summary>
    public bool AtuaNasVendas { get; set; } = true;

    /// <summary>
    /// Id do usuário no Notion (campo "Vendedor" dos cards). Liga o card ao usuário mesmo quando o Notion
    /// não informa o e-mail da pessoa — assim o lead não cai em "Vendedor não identificado".
    /// </summary>
    public string? NotionUserId { get; set; }

    /// <summary>
    /// Se preenchido, a distribuição automática só entrega a este consultor leads com um destes
    /// "O que?" (ProdutoInteresse), separados por vírgula — ex.: "AGV TRUCK". Nulo = recebe qualquer lead.
    /// </summary>
    public string? RecebeSomenteOQue { get; set; }

    /// <summary>Caminho da foto do vendedor (ex: /uploads/consultores/nome.jpeg), usada na página de obrigado pós-lead.</summary>
    public string? FotoUrl { get; set; }

    public DateTimeOffset CriadoEm { get; set; } = DateTimeOffset.UtcNow;
}
