using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

public interface ILeadAssignmentService
{
    /// <summary>
    /// Escolhe o próximo vendedor (papel Comercial, ativo) pra receber um lead sem responsável
    /// definido explicitamente: quem tem menos leads do tráfego pago recebidos no mês corrente
    /// (OrigemLead.VeioDoTrafegoPago — os do Notion não contam), respeitando o
    /// LimiteMensalLeads de cada um (quem já bateu o limite sai do rodízio até o mês virar).
    /// Retorna null se não houver ninguém elegível (nenhum vendedor ativo, ou todos no limite) —
    /// nesse caso o lead fica sem responsável, do mesmo jeito que fica sem etapa.
    /// </summary>
    /// <param name="oQue">"O que?" do lead (ProdutoInteresse): consultores com RecebeSomenteOQue só
    /// entram no rodízio de leads com um desses valores — e, nesses leads, têm a preferência.</param>
    Task<Guid?> ProximoResponsavelAsync(string? oQue, CancellationToken ct);

    /// <summary>
    /// Se um usuário específico pode receber mais um lead que está chegando (sincronização do Notion
    /// pelo "Vendedor" do card, planilha com o e-mail do responsável): precisa estar ativo e abaixo do
    /// LimiteMensalLeads definido pelo administrador — mesma contagem usada no rodízio.
    /// </summary>
    Task<bool> PodeReceberAsync(Guid usuarioId, CancellationToken ct);

    /// <summary>
    /// Distribui os leads do tráfego pago que ficaram sem responsável (ninguém disponível quando
    /// chegaram) — roda de tempos em tempos (DistribuicaoLeadsBackgroundService). Retorna quantos distribuiu.
    /// </summary>
    Task<int> DistribuirPendentesAsync(CancellationToken ct);

    /// <summary>Leads que passaram a ser do usuário depois de <paramref name="desde"/> (notificação de novo lead).</summary>
    Task<NovosLeadsDto> NovosLeadsAsync(Guid usuarioId, DateTimeOffset? desde, CancellationToken ct);
}
