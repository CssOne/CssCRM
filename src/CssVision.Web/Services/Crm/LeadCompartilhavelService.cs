using CssVision.Web.Api.Contracts.Crm;

namespace CssVision.Web.Services.Crm;

/// <summary>O pouco que se mostra de um lead ao compartilhá-lo numa conversa: nada de telefone, e-mail nem documento do cliente.</summary>
public record LeadCompartilhado(Guid Id, string Nome, string? Etapa, string? Responsavel, string? Produto, string? Regional);

public interface ILeadCompartilhavel
{
    /// <summary>
    /// O resumo do lead, só se quem está logado pode ver esse lead (a mesma regra da tela do lead: carteira/equipe). Sem acesso, a mesma
    /// exceção da tela do lead (não encontrado ou sem permissão).
    /// </summary>
    Task<LeadCompartilhado> ObterAsync(Guid leadId, CancellationToken ct);
}

public sealed class LeadCompartilhavelService(ILeadService leads) : ILeadCompartilhavel
{
    public async Task<LeadCompartilhado> ObterAsync(Guid leadId, CancellationToken ct)
    {
        var lead = await leads.ObterPorIdAsync(leadId, ct); // aplica o escopo de quem está logado
        return new LeadCompartilhado(lead.Id, lead.NomeOuRazaoSocial, lead.EtapaNome, lead.ResponsavelNome, lead.ProdutoInteresse, lead.Regional);
    }
}
