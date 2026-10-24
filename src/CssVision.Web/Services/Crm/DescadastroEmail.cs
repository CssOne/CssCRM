using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>Token do link "Cancelar inscrição" do rodapé do e-mail: carrega o id do lead, protegido (ninguém forja o de outro cliente).</summary>
public interface IDescadastroEmailTokens
{
    string Gerar(Guid leadId);
    Guid? Ler(string? token);
}

public sealed class DescadastroEmailTokens(IDataProtectionProvider protecao) : IDescadastroEmailTokens
{
    private IDataProtector Protetor => protecao.CreateProtector("CssVision.DescadastroEmail");

    public string Gerar(Guid leadId) => Protetor.Protect(leadId.ToString("N"));

    public Guid? Ler(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        try
        {
            return Guid.TryParseExact(Protetor.Unprotect(token), "N", out var id) ? id : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}

/// <summary>Descadastro dos e-mails do disparo: vale para todos os cards (leads) do cliente que têm o mesmo e-mail.</summary>
public static class DescadastroEmail
{
    /// <summary>Marca como descadastrado o e-mail do lead (e de todo lead com o mesmo e-mail). Devolve quantos cards foram marcados.</summary>
    public static async Task<int> DescadastrarAsync(ApplicationDbContext db, Guid leadId, CancellationToken ct)
    {
        var lead = await db.CrmLeads.AsNoTracking().Where(l => l.Id == leadId).Select(l => new { l.Id, l.Email, l.EmailNormalizado }).FirstOrDefaultAsync(ct);
        if (lead is null) return 0;
        // Os outros cards do cliente (um por veículo) podem trazer o e-mail só no texto, sem a versão normalizada.
        var email = (lead.EmailNormalizado ?? lead.Email)?.Trim().ToLowerInvariant();

        var agora = DateTimeOffset.UtcNow;
        var cards = await db.CrmLeads
            .Where(l => l.EmailDescadastradoEm == null
                && (l.Id == lead.Id || (email != null && (l.EmailNormalizado == email || (l.Email != null && l.Email.ToLower() == email)))))
            .ToListAsync(ct);
        foreach (var c in cards) c.EmailDescadastradoEm = agora;
        await db.SaveChangesAsync(ct);
        return cards.Count;
    }
}
