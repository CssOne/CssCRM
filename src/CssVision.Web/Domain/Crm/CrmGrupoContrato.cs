using CssVision.Web.Domain.Identity;

namespace CssVision.Web.Domain.Crm;

/// <summary>
/// Em que grupo o consultor estava a partir de um mês. Cada troca de grupo cria uma linha nova (a anterior fica como história), e o grupo vale até a próxima
/// linha. <see cref="GrupoId"/> nulo = saiu de qualquer grupo. É daqui que o Controle financeiro sabe como cobrar o consultor em cada mês.
/// </summary>
public class CrmGrupoMembro : CrmEntityBase
{
    public Guid UsuarioId { get; set; }
    public ApplicationUser Usuario { get; set; } = null!;

    public Guid? GrupoId { get; set; }
    public CrmGrupo? Grupo { get; set; }

    /// <summary>Primeiro dia do mês a partir do qual o consultor está neste grupo (e, nele, conta o início do contrato herdado).</summary>
    public DateOnly DesdeMes { get; set; }
}

/// <summary>
/// Contrato do grupo com vigência mensal: tipo (PJ/CLT), modelo PJ e transporte. Quem está no grupo e não tem contrato próprio herda isto.
/// </summary>
public class CrmGrupoContratoPerfil : CrmEntityBase
{
    public Guid GrupoId { get; set; }
    public CrmGrupo Grupo { get; set; } = null!;

    /// <summary>Primeiro dia do mês de vigência (vale até a próxima linha do grupo).</summary>
    public DateOnly VigenteDesde { get; set; }

    public TipoContrato? Tipo { get; set; }
    public ModeloRemuneracaoPj? ModeloPj { get; set; }
    public ModalidadeTransporte Transporte { get; set; }

    public Guid DefinidoPorId { get; set; }
}

/// <summary>
/// Valor do grupo para um parâmetro (coworking, alimentação, passagem...), com vigência a partir de um mês. O consultor pode ter o dele
/// (<see cref="CrmFinanceiroValor"/>), que vale por cima; salário e recorrência são sempre individuais.
/// </summary>
public class CrmGrupoContratoValor : CrmEntityBase
{
    public Guid GrupoId { get; set; }
    public CrmGrupo Grupo { get; set; } = null!;

    public ParametroFinanceiro Parametro { get; set; }
    public DateOnly VigenteDesde { get; set; }
    public decimal Valor { get; set; }
    public string? Observacao { get; set; }
    public Guid DefinidoPorId { get; set; }
}

/// <summary>Carência do grupo para começar a cobrar um custo fixo, em meses a contar da entrada do consultor no grupo. Nulo = segue o padrão geral.</summary>
public class CrmGrupoContratoPrazo : CrmEntityBase
{
    public Guid GrupoId { get; set; }
    public CrmGrupo Grupo { get; set; } = null!;

    public ParametroFinanceiro Parametro { get; set; }
    public int? CarenciaMeses { get; set; }
    public Guid DefinidoPorId { get; set; }
}
