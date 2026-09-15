using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

public sealed class LookupService(ApplicationDbContext db, ICurrentUserService currentUser) : ILookupService
{
    public async Task<IReadOnlyList<PipelineStageDto>> ObterEtapasAsync(CancellationToken ct) =>
        await db.CrmPipelineStages.AsNoTracking()
            .OrderBy(s => s.Ordem)
            .Select(s => new PipelineStageDto(s.Id, s.Nome, s.Ordem, s.Tipo, s.Cor, s.Ativa))
            .ToListAsync(ct);

    public async Task<PipelineStageDto> CriarEtapaAsync(CreateStageRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (await db.CrmPipelineStages.AnyAsync(s => s.Nome == request.Nome, ct))
        {
            throw new CrmBusinessException("Já existe uma etapa com este nome.", "etapa_duplicada");
        }

        var etapa = new CrmPipelineStage
        {
            Nome = request.Nome.Trim(),
            Ordem = request.Ordem,
            Tipo = TipoEtapaPipeline.Aberta,
            Cor = request.Cor
        };

        db.CrmPipelineStages.Add(etapa);
        await db.SaveChangesAsync(ct);

        return new PipelineStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Tipo, etapa.Cor, etapa.Ativa);
    }

    public async Task<IReadOnlyList<LossReasonDto>> ObterMotivosPerdaAsync(CancellationToken ct) =>
        await db.CrmLossReasons.AsNoTracking()
            .Where(m => m.Ativo)
            .OrderBy(m => m.Descricao)
            .Select(m => new LossReasonDto(m.Id, m.Descricao, m.Ativo))
            .ToListAsync(ct);

    public async Task<LossReasonDto> CriarMotivoPerdaAsync(CreateLossReasonRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (await db.CrmLossReasons.AnyAsync(m => m.Descricao == request.Descricao, ct))
        {
            throw new CrmBusinessException("Já existe um motivo de perda com esta descrição.", "motivo_duplicado");
        }

        var motivo = new CrmLossReason { Descricao = request.Descricao.Trim() };
        db.CrmLossReasons.Add(motivo);
        await db.SaveChangesAsync(ct);

        return new LossReasonDto(motivo.Id, motivo.Descricao, motivo.Ativo);
    }

    public async Task<IReadOnlyList<LeadStageDto>> ObterEtapasLeadAsync(CancellationToken ct) =>
        await db.CrmLeadStages.AsNoTracking()
            .Where(s => s.Ativa)
            .OrderBy(s => s.Ordem)
            .Select(s => new LeadStageDto(s.Id, s.Nome, s.Ordem, s.Cor, s.Fechada, s.Ativa))
            .ToListAsync(ct);

    public async Task<LeadStageDto> CriarEtapaLeadAsync(CreateLeadStageRequest request, CancellationToken ct)
    {
        ExigirGestaoComercial();

        if (await db.CrmLeadStages.AnyAsync(s => s.Nome == request.Nome, ct))
        {
            throw new CrmBusinessException("Já existe uma etapa de lead com este nome.", "etapa_duplicada");
        }

        var etapa = new CrmLeadStage
        {
            Nome = request.Nome.Trim(),
            Ordem = request.Ordem,
            Cor = request.Cor,
            Fechada = request.Fechada
        };

        db.CrmLeadStages.Add(etapa);
        await db.SaveChangesAsync(ct);

        return new LeadStageDto(etapa.Id, etapa.Nome, etapa.Ordem, etapa.Cor, etapa.Fechada, etapa.Ativa);
    }

    public async Task<IReadOnlyList<string>> ObterOrigensAsync(CancellationToken ct) =>
        await db.CrmLeads.AsNoTracking()
            .Where(l => !l.Arquivado && l.Origem != null)
            .Select(l => l.Origem!)
            .Distinct()
            .OrderBy(o => o)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RegionalDto>> ObterRegionaisAsync(CancellationToken ct) =>
        await db.CrmRegionais.AsNoTracking()
            .OrderBy(r => r.Nome)
            .Select(r => new RegionalDto(r.Id, r.Nome, r.Ativa, r.Usuarios.Count))
            .ToListAsync(ct);

    public async Task<RegionalDto> CriarRegionalAsync(CreateRegionalRequest request, CancellationToken ct)
    {
        ExigirVisaoTotal();

        if (await db.CrmRegionais.AnyAsync(r => r.Nome == request.Nome, ct))
        {
            throw new CrmBusinessException("Já existe uma regional com este nome.", "regional_duplicada");
        }

        var regional = new CrmRegional { Nome = request.Nome.Trim() };
        db.CrmRegionais.Add(regional);
        await db.SaveChangesAsync(ct);

        return new RegionalDto(regional.Id, regional.Nome, regional.Ativa, 0);
    }

    public async Task<RegionalDto> AtualizarRegionalAsync(Guid id, UpdateRegionalRequest request, CancellationToken ct)
    {
        ExigirVisaoTotal();

        var regional = await db.CrmRegionais.FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new CrmNotFoundException("Regional", id);

        if (await db.CrmRegionais.AnyAsync(r => r.Id != id && r.Nome == request.Nome, ct))
        {
            throw new CrmBusinessException("Já existe uma regional com este nome.", "regional_duplicada");
        }

        regional.Nome = request.Nome.Trim();
        regional.Ativa = request.Ativa;
        await db.SaveChangesAsync(ct);

        var quantidadeUsuarios = await db.Users.CountAsync(u => u.RegionalId == id, ct);
        return new RegionalDto(regional.Id, regional.Nome, regional.Ativa, quantidadeUsuarios);
    }

    public async Task<IReadOnlyList<GrupoDto>> ObterGruposAsync(Guid? regionalId, CancellationToken ct)
    {
        var regionalAlvo = await ResolverRegionalAlvoAsync(regionalId, ct);
        if (regionalAlvo is null) return [];

        var grupos = await db.CrmGrupos.AsNoTracking()
            .Where(g => g.RegionalId == regionalAlvo)
            .Include(g => g.Usuarios)
            .OrderBy(g => g.Nome)
            .ToListAsync(ct);

        return grupos.Select(ParaGrupoDto).ToList();
    }

    public async Task<GrupoDto> CriarGrupoAsync(CreateGrupoRequest request, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem gerenciar grupos.");
        }

        Guid regionalId;
        if (currentUser.TemVisaoTotal)
        {
            if (request.RegionalId is null)
            {
                throw new CrmBusinessException("Informe a regional deste grupo.", "regional_obrigatoria");
            }

            regionalId = request.RegionalId.Value;
        }
        else
        {
            var regionalAtual = await ObterRegionalAtualAsync(ct)
                ?? throw new CrmBusinessException("Sua conta ainda não está vinculada a uma regional — peça a um administrador para configurá-la antes de criar grupos.", "sem_regional");

            if (request.RegionalId.HasValue && request.RegionalId != regionalAtual)
            {
                throw new CrmForbiddenException("Você só pode criar grupos na sua própria regional.");
            }

            regionalId = regionalAtual;
        }

        if (!await db.CrmRegionais.AnyAsync(r => r.Id == regionalId, ct))
        {
            throw new CrmNotFoundException("Regional", regionalId);
        }

        if (await db.CrmGrupos.AnyAsync(g => g.RegionalId == regionalId && g.Nome == request.Nome, ct))
        {
            throw new CrmBusinessException("Já existe um grupo com este nome nesta regional.", "grupo_duplicado");
        }

        var grupo = new CrmGrupo { RegionalId = regionalId, Nome = request.Nome.Trim() };
        db.CrmGrupos.Add(grupo);
        await db.SaveChangesAsync(ct);

        return ParaGrupoDto(grupo);
    }

    public async Task<GrupoDto> AtualizarGrupoAsync(Guid id, UpdateGrupoRequest request, CancellationToken ct)
    {
        var grupo = await db.CrmGrupos.Include(g => g.Usuarios).FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new CrmNotFoundException("Grupo", id);

        await ExigirAcessoRegionalAsync(grupo.RegionalId, ct);

        if (await db.CrmGrupos.AnyAsync(g => g.Id != id && g.RegionalId == grupo.RegionalId && g.Nome == request.Nome, ct))
        {
            throw new CrmBusinessException("Já existe um grupo com este nome nesta regional.", "grupo_duplicado");
        }

        grupo.Nome = request.Nome.Trim();
        grupo.Ativo = request.Ativo;
        await db.SaveChangesAsync(ct);

        return ParaGrupoDto(grupo);
    }

    public async Task<GrupoDto> AtualizarMembrosGrupoAsync(Guid id, UpdateGrupoMembrosRequest request, CancellationToken ct)
    {
        var grupo = await db.CrmGrupos.Include(g => g.Usuarios).FirstOrDefaultAsync(g => g.Id == id, ct)
            ?? throw new CrmNotFoundException("Grupo", id);

        await ExigirAcessoRegionalAsync(grupo.RegionalId, ct);

        var novosIds = request.ConsultorIds.Distinct().ToList();

        if (novosIds.Count > 0 && await db.Users.AsNoTracking().AnyAsync(u => novosIds.Contains(u.Id) && u.RegionalId != grupo.RegionalId, ct))
        {
            throw new CrmBusinessException("Todos os consultores selecionados devem pertencer à mesma regional do grupo.", "consultor_regional_invalida");
        }

        var idsAtuais = grupo.Usuarios.Select(u => u.Id).ToHashSet();

        foreach (var usuario in grupo.Usuarios.Where(u => !novosIds.Contains(u.Id)))
        {
            usuario.GrupoId = null;
        }

        var idsParaAdicionar = novosIds.Where(uid => !idsAtuais.Contains(uid)).ToList();
        if (idsParaAdicionar.Count > 0)
        {
            var usuariosParaAdicionar = await db.Users.Where(u => idsParaAdicionar.Contains(u.Id)).ToListAsync(ct);
            foreach (var usuario in usuariosParaAdicionar)
            {
                usuario.GrupoId = grupo.Id;
            }
        }

        await db.SaveChangesAsync(ct);

        var atualizado = await db.CrmGrupos.AsNoTracking().Include(g => g.Usuarios).FirstAsync(g => g.Id == id, ct);
        return ParaGrupoDto(atualizado);
    }

    private static GrupoDto ParaGrupoDto(CrmGrupo grupo) => new(
        grupo.Id, grupo.RegionalId, grupo.Nome, grupo.Ativo,
        grupo.Usuarios.Where(u => u.Ativo).OrderBy(u => u.NomeCompleto)
            .Select(u => new GrupoMembroDto(u.Id, u.NomeCompleto, u.FotoUrl)).ToList());

    private async Task<Guid?> ObterRegionalAtualAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.RegionalId).FirstOrDefaultAsync(ct);

    /// <summary>Resolve a regional cujos grupos devem ser lidos: Admin/GestorMaster escolhem via parâmetro;
    /// GestorComercial sempre enxerga a própria (ignorando um parâmetro divergente).</summary>
    private async Task<Guid?> ResolverRegionalAlvoAsync(Guid? regionalId, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem visualizar grupos.");
        }

        if (currentUser.TemVisaoTotal) return regionalId;

        var regionalAtual = await ObterRegionalAtualAsync(ct);
        return regionalAtual;
    }

    private async Task ExigirAcessoRegionalAsync(Guid regionalId, CancellationToken ct)
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem gerenciar grupos.");
        }

        if (currentUser.TemVisaoTotal) return;

        var regionalAtual = await ObterRegionalAtualAsync(ct);
        if (regionalAtual is null || regionalAtual != regionalId)
        {
            throw new CrmForbiddenException("Você só pode gerenciar grupos da sua própria regional.");
        }
    }

    private void ExigirGestaoComercial()
    {
        if (!currentUser.PodeGerirComercial)
        {
            throw new CrmForbiddenException("Apenas gestores comerciais podem alterar as configurações do funil.");
        }
    }

    private void ExigirVisaoTotal()
    {
        if (!currentUser.TemVisaoTotal)
        {
            throw new CrmForbiddenException("Apenas administradores podem gerenciar regionais.");
        }
    }
}
