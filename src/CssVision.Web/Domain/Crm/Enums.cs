namespace CssVision.Web.Domain.Crm;

public enum TipoPessoa
{
    Fisica = 1,
    Juridica = 2
}

public enum TipoEtapaPipeline
{
    Aberta = 1,
    Ganho = 2,
    Perdido = 3
}

public enum TipoAtividade
{
    Ligacao = 1,
    WhatsApp = 2,
    Email = 3,
    Reuniao = 4,
    Visita = 5,
    Retorno = 6,
    Tarefa = 7,
    Observacao = 8
}

public enum StatusAtividade
{
    Pendente = 1,
    Concluida = 2,
    Cancelada = 3
}

public enum TipoEventoTimeline
{
    LeadCriado = 1,
    LeadAtualizado = 2,
    TrocaResponsavel = 3,
    MudancaEtapa = 4,
    Atividade = 5,
    Nota = 6,
    Proposta = 7,
    OportunidadeGanha = 8,
    OportunidadePerdida = 9,
    OportunidadeCriada = 10
}
