using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public interface IAvisosPagamentoService
{
    Task<IReadOnlyList<AvisoPagamentoDto>> EnviarAsync(AvisoPagamentoCreateRequest request, CancellationToken ct);
    Task<IReadOnlyList<AvisoPagamentoDto>> ListarAsync(StatusAvisoConsultor? status, Guid? consultorId, CancellationToken ct);
    Task<AvisoPagamentoDto> ResolverAsync(Guid id, CancellationToken ct);

    /// <summary>Manda de novo a notificação (push e aviso na tela) de um aviso ainda em aberto.</summary>
    Task<AvisoPagamentoDto> ReenviarAsync(Guid id, CancellationToken ct);

    /// <summary>Avisos em aberto por consultor, dentro do que quem chama enxerga.</summary>
    Task<IReadOnlyList<AvisoResumoConsultorDto>> ResumoAsync(CancellationToken ct);
    Task<IReadOnlyList<MeuAvisoDto>> MeusAsync(CancellationToken ct);
    Task MarcarCienteAsync(Guid id, CancellationToken ct);
}

/// <summary>
/// Avisos de pagamento em aberto: gestão e financeiro avisam os consultores que enxergam (o Financeiro e o Gestor regional, só os da
/// regional deles). O consultor recebe push, aviso dentro do CRM e o card "Avisos importantes" do Portal, até o aviso ser resolvido.
/// </summary>
public sealed class AvisosPagamentoService(
    ApplicationDbContext db,
    IEquipeComercialService equipe,
    ICurrentUserService currentUser,
    IPushService push,
    IAuditSink audit,
    ILogger<AvisosPagamentoService> logger,
    ICrmEventHub? eventos = null) : IAvisosPagamentoService
{
    private const string TituloPadrao = "Pagamento em aberto";

    private void ExigirQuemAvisa()
    {
        if (!Roles.GestaoFinanceira.Any(currentUser.IsInRole))
        {
            throw new CrmForbiddenException("Apenas a gestão comercial e o financeiro enviam avisos de pagamento.");
        }
    }

    public async Task<IReadOnlyList<AvisoPagamentoDto>> EnviarAsync(AvisoPagamentoCreateRequest request, CancellationToken ct)
    {
        ExigirQuemAvisa();
        var mensagem = request.Mensagem?.Trim() ?? "";
        if (mensagem.Length < 3) throw new CrmBusinessException("Escreva a mensagem do aviso.", "mensagem_obrigatoria");
        if (request.Valor is < 0) throw new CrmBusinessException("O valor não pode ser negativo.", "valor_invalido");

        var ids = request.ConsultorIds.Where(i => i != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) throw new CrmBusinessException("Escolha ao menos um consultor.", "consultor_obrigatorio");

        // Só consultores (papel Comercial), ativos e dentro do que quem envia enxerga.
        var consultores = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && u.Ativo)
            .Where(u => db.UserRoles.Any(ur => ur.UserId == u.Id && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == Roles.Comercial)))
            .Select(u => new { u.Id, u.NomeCompleto })
            .ToListAsync(ct);
        foreach (var consultor in consultores)
        {
            if (!await equipe.PodeAcessarVendedorAsync(consultor.Id, ct))
            {
                throw new CrmForbiddenException($"Você não pode enviar aviso para {consultor.NomeCompleto}.");
            }
        }
        if (consultores.Count != ids.Count)
        {
            throw new CrmBusinessException("Só é possível avisar consultores ativos.", "consultor_invalido");
        }

        var titulo = string.IsNullOrWhiteSpace(request.Titulo) ? TituloPadrao : request.Titulo.Trim();
        var referencia = string.IsNullOrWhiteSpace(request.Referencia) ? null : request.Referencia.Trim();
        var avisos = consultores.Select(c => new CrmAvisoConsultor
        {
            ConsultorId = c.Id, Titulo = titulo, Mensagem = mensagem, Valor = request.Valor, Referencia = referencia, EnviadoPorId = currentUser.UserId,
        }).ToList();
        db.CrmAvisosConsultor.AddRange(avisos);
        await db.SaveChangesAsync(ct);

        foreach (var aviso in avisos)
        {
            await NotificarAsync(aviso, ct);
            await audit.RegistrarAsync("AvisoPagamentoEnviado", nameof(CrmAvisoConsultor), aviso.Id, new { aviso.ConsultorId, aviso.Valor }, ct);
        }
        eventos?.PublicarQuadroAtualizado("avisos");

        return await MontarAsync(avisos, ct);
    }

    public async Task<IReadOnlyList<AvisoPagamentoDto>> ListarAsync(StatusAvisoConsultor? status, Guid? consultorId, CancellationToken ct)
    {
        ExigirQuemAvisa();
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        var query = db.CrmAvisosConsultor.AsNoTracking().AsQueryable();
        if (visiveis is not null) query = query.Where(a => visiveis.Contains(a.ConsultorId));
        if (status is not null) query = query.Where(a => a.Status == status);
        if (consultorId is not null) query = query.Where(a => a.ConsultorId == consultorId);
        var avisos = await query.OrderByDescending(a => a.CriadoEm).Take(300).ToListAsync(ct);
        return await MontarAsync(avisos, ct);
    }

    public async Task<AvisoPagamentoDto> ResolverAsync(Guid id, CancellationToken ct)
    {
        ExigirQuemAvisa();
        var aviso = await db.CrmAvisosConsultor.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new CrmNotFoundException("Aviso", id);
        if (!await equipe.PodeAcessarVendedorAsync(aviso.ConsultorId, ct)) throw new CrmNotFoundException("Aviso", id);

        if (aviso.Status != StatusAvisoConsultor.Resolvido)
        {
            aviso.Status = StatusAvisoConsultor.Resolvido;
            aviso.ResolvidoEm = DateTimeOffset.UtcNow;
            aviso.ResolvidoPorId = currentUser.UserId;
            await db.SaveChangesAsync(ct);
            await audit.RegistrarAsync("AvisoPagamentoResolvido", nameof(CrmAvisoConsultor), aviso.Id, new { aviso.ConsultorId }, ct);
            eventos?.PublicarQuadroAtualizado("avisos");
        }
        return (await MontarAsync([aviso], ct))[0];
    }

    public async Task<AvisoPagamentoDto> ReenviarAsync(Guid id, CancellationToken ct)
    {
        ExigirQuemAvisa();
        var aviso = await db.CrmAvisosConsultor.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw new CrmNotFoundException("Aviso", id);
        if (!await equipe.PodeAcessarVendedorAsync(aviso.ConsultorId, ct)) throw new CrmNotFoundException("Aviso", id);
        if (aviso.Status != StatusAvisoConsultor.Aberto) throw new CrmBusinessException("Este aviso já foi resolvido.", "aviso_resolvido");

        // Volta a aparecer como novidade para o consultor (sem "ciente") e a notificação sai de novo.
        aviso.LidoEm = null;
        await db.SaveChangesAsync(ct);
        await NotificarAsync(aviso, ct, lembrete: true);
        await audit.RegistrarAsync("AvisoPagamentoReenviado", nameof(CrmAvisoConsultor), aviso.Id, new { aviso.ConsultorId }, ct);
        eventos?.PublicarQuadroAtualizado("avisos");
        return (await MontarAsync([aviso], ct))[0];
    }

    public async Task<IReadOnlyList<AvisoResumoConsultorDto>> ResumoAsync(CancellationToken ct)
    {
        ExigirQuemAvisa();
        var visiveis = await equipe.ObterVendedoresVisiveisAsync(ct);
        var query = db.CrmAvisosConsultor.AsNoTracking().Where(a => a.Status == StatusAvisoConsultor.Aberto);
        if (visiveis is not null) query = query.Where(a => visiveis.Contains(a.ConsultorId));
        return await query.GroupBy(a => a.ConsultorId).Select(g => new AvisoResumoConsultorDto(g.Key, g.Count())).ToListAsync(ct);
    }

    private async Task NotificarAsync(CrmAvisoConsultor aviso, CancellationToken ct, bool lembrete = false)
    {
        try
        {
            var corpo = aviso.Valor is { } v ? $"{aviso.Mensagem} · R$ {v:N2}" : aviso.Mensagem;
            var titulo = lembrete ? $"Lembrete: {aviso.Titulo}" : aviso.Titulo;
            await push.EnviarAsync(aviso.ConsultorId, new PushMensagem(titulo, corpo, "/app/portal", $"aviso-{aviso.Id}"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar o push do aviso {AvisoId} ao consultor {ConsultorId}.", aviso.Id, aviso.ConsultorId);
        }
    }

    public async Task<IReadOnlyList<MeuAvisoDto>> MeusAsync(CancellationToken ct)
    {
        var avisos = await db.CrmAvisosConsultor.AsNoTracking()
            .Where(a => a.ConsultorId == currentUser.UserId && a.Status == StatusAvisoConsultor.Aberto)
            .OrderByDescending(a => a.CriadoEm).Take(50).ToListAsync(ct);
        var nomes = await NomesAsync(avisos.Select(a => a.EnviadoPorId), ct);
        return avisos.Select(a => new MeuAvisoDto(a.Id, a.Titulo, a.Mensagem, a.Valor, a.Referencia,
            nomes.GetValueOrDefault(a.EnviadoPorId, "Financeiro"), a.CriadoEm, a.LidoEm)).ToList();
    }

    public async Task MarcarCienteAsync(Guid id, CancellationToken ct)
    {
        // Só o próprio consultor; aviso de outra pessoa se comporta como inexistente.
        var aviso = await db.CrmAvisosConsultor.FirstOrDefaultAsync(a => a.Id == id && a.ConsultorId == currentUser.UserId, ct)
            ?? throw new CrmNotFoundException("Aviso", id);
        if (aviso.LidoEm is null)
        {
            aviso.LidoEm = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    private async Task<Dictionary<Guid, string>> NomesAsync(IEnumerable<Guid> ids, CancellationToken ct)
    {
        var lista = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => lista.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NomeCompleto, ct);
    }

    private async Task<IReadOnlyList<AvisoPagamentoDto>> MontarAsync(List<CrmAvisoConsultor> avisos, CancellationToken ct)
    {
        var nomes = await NomesAsync(avisos.Select(a => a.ConsultorId).Concat(avisos.Select(a => a.EnviadoPorId)), ct);
        string Nome(Guid id) => nomes.GetValueOrDefault(id, "Usuário removido");
        return avisos.Select(a => new AvisoPagamentoDto(a.Id, a.ConsultorId, Nome(a.ConsultorId), a.Titulo, a.Mensagem, a.Valor, a.Referencia,
            Nome(a.EnviadoPorId), a.CriadoEm, a.Status, a.LidoEm, a.ResolvidoEm)).ToList();
    }
}
