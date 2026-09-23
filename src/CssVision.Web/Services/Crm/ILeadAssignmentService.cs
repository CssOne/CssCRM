namespace CssVision.Web.Services.Crm;

public interface ILeadAssignmentService
{
    /// <summary>
    /// Escolhe o próximo vendedor (papel Comercial, ativo) pra receber um lead sem responsável
    /// definido explicitamente: quem tem menos leads recebidos no mês corrente, respeitando o
    /// LimiteMensalLeads de cada um (quem já bateu o limite sai do rodízio até o mês virar).
    /// Retorna null se não houver ninguém elegível (nenhum vendedor ativo, ou todos no limite) —
    /// nesse caso o lead fica sem responsável, do mesmo jeito que fica sem etapa.
    /// </summary>
    Task<Guid?> ProximoResponsavelAsync(CancellationToken ct);

    /// <summary>
    /// Se um usuário específico pode receber mais um lead que está chegando (sincronização do Notion
    /// pelo "Vendedor" do card, planilha com o e-mail do responsável): precisa estar ativo e abaixo do
    /// LimiteMensalLeads definido pelo administrador — mesma contagem usada no rodízio.
    /// </summary>
    Task<bool> PodeReceberAsync(Guid usuarioId, CancellationToken ct);
}
