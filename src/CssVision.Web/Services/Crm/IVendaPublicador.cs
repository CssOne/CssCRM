namespace CssVision.Web.Services.Crm;

/// <summary>
/// Gancho "uma venda acabou de ser fechada": quem implementa (hoje, o aviso no canal do Discord) decide o que fazer. É um extra: o serviço
/// de oportunidades chama depois de salvar a venda e ignora qualquer falha — nunca atrapalha o registro da venda.
/// </summary>
public interface IVendaPublicador
{
    Task PublicarVendaAsync(Guid oportunidadeId, CancellationToken ct);
}
