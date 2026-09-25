using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Gestão de usuários (consultores, gestores comerciais e administradores). Admin/GestorMaster têm
/// acesso irrestrito; um GestorComercial só cria/edita usuários com papel Comercial dentro da
/// própria regional — nunca outros gestores/administradores nem consultores de outra regional.
/// Nenhum usuário é excluído fisicamente: desativação é feita via <see cref="ApplicationUser.Ativo"/>.
/// </summary>
public sealed class UserManagementService(
    ApplicationDbContext db,
    UserManager<ApplicationUser> userManager,
    ICurrentUserService currentUser,
    IAuditSink audit) : IUserManagementService
{
    public async Task<PagedResult<UserSummaryDto>> ListarAsync(UserFilterRequest filtro, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking()
            .Include(u => u.Regional)
            .Include(u => u.GestorComercial)
            .Include(u => u.Grupo)
            .AsQueryable();

        if (!currentUser.TemVisaoTotal)
        {
            var regionalId = await ObterRegionalAtualAsync(ct);
            query = regionalId is null ? query.Where(u => false) : query.Where(u => u.RegionalId == regionalId);
        }

        if (!string.IsNullOrWhiteSpace(filtro.Busca))
        {
            var busca = filtro.Busca.Trim();
            query = query.Where(u => EF.Functions.ILike(u.NomeCompleto, $"%{busca}%") || EF.Functions.ILike(u.Email!, $"%{busca}%"));
        }

        if (filtro.RegionalId.HasValue) query = query.Where(u => u.RegionalId == filtro.RegionalId);
        if (filtro.GrupoId.HasValue) query = query.Where(u => u.GrupoId == filtro.GrupoId);
        if (filtro.Ativo.HasValue) query = query.Where(u => u.Ativo == filtro.Ativo);

        if (!string.IsNullOrWhiteSpace(filtro.Papel))
        {
            var idsComPapel = await userManager.GetUsersInRoleAsync(filtro.Papel);
            var ids = idsComPapel.Select(u => u.Id).ToList();
            query = query.Where(u => ids.Contains(u.Id));
        }

        var total = await query.CountAsync(ct);

        var pagina = await query
            .OrderBy(u => u.NomeCompleto)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .ToListAsync(ct);

        var itens = new List<UserSummaryDto>();
        foreach (var usuario in pagina)
        {
            itens.Add(await ParaDtoAsync(usuario, ct));
        }

        return new PagedResult<UserSummaryDto>
        {
            Itens = itens,
            Pagina = filtro.Pagina,
            TamanhoPagina = filtro.TamanhoPagina,
            TotalRegistros = total
        };
    }

    public async Task<UserSummaryDto> ObterPorIdAsync(Guid id, CancellationToken ct)
    {
        var usuario = await CarregarComEscopoAsync(id, ct);
        return await ParaDtoAsync(usuario, ct);
    }

    public async Task<UserSummaryDto> CriarAsync(UserCreateRequest request, CancellationToken ct)
    {
        if (!Roles.All.Contains(request.Papel))
        {
            throw new CrmBusinessException("Papel inválido.", "papel_invalido");
        }

        Guid? regionalId = request.RegionalId;
        Guid? gestorComercialId = request.GestorComercialId;

        if (!currentUser.TemVisaoTotal)
        {
            if (request.Papel != Roles.Comercial)
            {
                throw new CrmForbiddenException("Você só pode cadastrar consultores (papel Comercial).");
            }

            var regionalAtual = await ObterRegionalAtualAsync(ct)
                ?? throw new CrmBusinessException("Sua conta ainda não está vinculada a uma regional — peça a um administrador para configurá-la antes de cadastrar consultores.", "sem_regional");

            regionalId = regionalAtual;
            gestorComercialId = currentUser.UserId;
        }
        else if (request.Papel is Roles.Comercial or Roles.GestorComercial && regionalId is null)
        {
            throw new CrmBusinessException("Informe a regional deste usuário.", "regional_obrigatoria");
        }

        if (regionalId.HasValue && !await db.CrmRegionais.AnyAsync(r => r.Id == regionalId, ct))
        {
            throw new CrmNotFoundException("Regional", regionalId.Value);
        }

        if (gestorComercialId.HasValue)
        {
            var gestor = await userManager.FindByIdAsync(gestorComercialId.Value.ToString())
                ?? throw new CrmNotFoundException("Gestor comercial", gestorComercialId.Value);
            if (!await userManager.IsInRoleAsync(gestor, Roles.GestorComercial))
            {
                throw new CrmBusinessException("O usuário informado como gestor não possui o papel GestorComercial.", "gestor_invalido");
            }
        }

        var grupoId = await ValidarGrupoAsync(request.GrupoId, regionalId, ct);

        var usuario = new ApplicationUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = true,
            NomeCompleto = request.NomeCompleto.Trim(),
            PhoneNumber = DocumentValidation.NormalizarTelefone(request.Telefone),
            RegionalId = regionalId,
            GestorComercialId = gestorComercialId,
            GrupoId = grupoId,
            LimiteMensalLeads = request.Papel == Roles.Comercial ? request.LimiteMensalLeads : null,
            RecebeSomenteOQue = request.Papel == Roles.Comercial ? FiltroOQue.Juntar(request.RecebeSomenteOQue) : null,
        };

        var resultado = await userManager.CreateAsync(usuario, request.Senha);
        if (!resultado.Succeeded)
        {
            throw new CrmBusinessException(IdentityErrors.Traduzir(resultado), "usuario_invalido");
        }

        await userManager.AddToRoleAsync(usuario, request.Papel);
        await audit.RegistrarAsync("UsuarioCriado", nameof(ApplicationUser), usuario.Id, new { usuario.NomeCompleto, Papel = request.Papel }, ct);

        return await ParaDtoAsync(usuario, ct);
    }

    public async Task<UserSummaryDto> AtualizarAsync(Guid id, UserUpdateRequest request, CancellationToken ct)
    {
        var usuario = await CarregarComEscopoAsync(id, ct);

        if (!Roles.All.Contains(request.Papel))
        {
            throw new CrmBusinessException("Papel inválido.", "papel_invalido");
        }

        var regionalId = request.RegionalId;
        var gestorComercialId = request.GestorComercialId;

        if (!currentUser.TemVisaoTotal)
        {
            if (request.Papel != Roles.Comercial)
            {
                throw new CrmForbiddenException("Você só pode gerenciar consultores (papel Comercial).");
            }

            var regionalAtual = await ObterRegionalAtualAsync(ct);
            if (regionalAtual is null || regionalId != regionalAtual)
            {
                throw new CrmForbiddenException("Você só pode gerenciar usuários da sua própria regional.");
            }

            if (id == currentUser.UserId)
            {
                throw new CrmForbiddenException("Você não pode alterar o próprio cadastro por aqui.");
            }
        }
        else if (id == currentUser.UserId && !request.Ativo)
        {
            throw new CrmBusinessException("Você não pode desativar a própria conta.", "auto_desativacao");
        }

        if (regionalId.HasValue && !await db.CrmRegionais.AnyAsync(r => r.Id == regionalId, ct))
        {
            throw new CrmNotFoundException("Regional", regionalId.Value);
        }

        if (gestorComercialId.HasValue)
        {
            if (gestorComercialId == id)
            {
                throw new CrmBusinessException("Um usuário não pode ser gestor de si mesmo.", "gestor_invalido");
            }

            var gestor = await userManager.FindByIdAsync(gestorComercialId.Value.ToString())
                ?? throw new CrmNotFoundException("Gestor comercial", gestorComercialId.Value);
            if (!await userManager.IsInRoleAsync(gestor, Roles.GestorComercial))
            {
                throw new CrmBusinessException("O usuário informado como gestor não possui o papel GestorComercial.", "gestor_invalido");
            }
        }

        var grupoId = await ValidarGrupoAsync(request.GrupoId, regionalId, ct);

        usuario.NomeCompleto = request.NomeCompleto.Trim();
        usuario.PhoneNumber = DocumentValidation.NormalizarTelefone(request.Telefone);
        usuario.RegionalId = regionalId;
        usuario.GestorComercialId = gestorComercialId;
        usuario.GrupoId = grupoId;
        usuario.LimiteMensalLeads = request.Papel == Roles.Comercial ? request.LimiteMensalLeads : null;
        // Só quando vier (lista vazia limpa): outras telas que editam o usuário não mandam o campo.
        if (request.Papel != Roles.Comercial) usuario.RecebeSomenteOQue = null;
        else if (request.RecebeSomenteOQue is not null) usuario.RecebeSomenteOQue = FiltroOQue.Juntar(request.RecebeSomenteOQue);
        usuario.Ativo = request.Ativo;

        var papeisAtuais = await userManager.GetRolesAsync(usuario);
        if (!papeisAtuais.Contains(request.Papel))
        {
            await userManager.RemoveFromRolesAsync(usuario, papeisAtuais);
            await userManager.AddToRoleAsync(usuario, request.Papel);
        }

        await db.SaveChangesAsync(ct);
        await audit.RegistrarAsync("UsuarioAtualizado", nameof(ApplicationUser), usuario.Id, new { usuario.NomeCompleto, usuario.Ativo }, ct);

        return await ParaDtoAsync(usuario, ct);
    }

    public async Task RedefinirSenhaAsync(Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var usuario = await CarregarComEscopoAsync(id, ct);

        if (!currentUser.TemVisaoTotal && !await userManager.IsInRoleAsync(usuario, Roles.Comercial))
        {
            throw new CrmForbiddenException("Você só pode redefinir a senha de consultores da sua regional.");
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(usuario);
        var resultado = await userManager.ResetPasswordAsync(usuario, token, request.NovaSenha);
        if (!resultado.Succeeded)
        {
            throw new CrmBusinessException(IdentityErrors.Traduzir(resultado), "senha_invalida");
        }

        await audit.RegistrarAsync("SenhaRedefinida", nameof(ApplicationUser), usuario.Id, null, ct);
    }

    public async Task ExcluirAsync(Guid id, CancellationToken ct)
    {
        if (!currentUser.TemVisaoTotal)
        {
            throw new CrmForbiddenException("Apenas administradores podem excluir usuários.");
        }

        if (id == currentUser.UserId)
        {
            throw new CrmBusinessException("Você não pode excluir a própria conta.", "auto_exclusao");
        }

        var usuario = await userManager.FindByIdAsync(id.ToString())
            ?? throw new CrmNotFoundException("Usuário", id);

        var possuiVinculos =
            await db.CrmLeads.AnyAsync(l => l.ResponsavelId == id, ct) ||
            await db.CrmActivities.AnyAsync(a => a.ResponsavelId == id, ct) ||
            await db.CrmVeiculos.AnyAsync(v => v.VistoriadorId == id, ct) ||
            await db.CrmSalesGoals.AnyAsync(g => g.VendedorId == id, ct) ||
            await db.Users.AnyAsync(u => u.GestorComercialId == id, ct);

        if (possuiVinculos)
        {
            throw new CrmBusinessException(
                "Não é possível excluir este usuário porque existem leads, oportunidades, atividades, metas ou consultores vinculados a ele. Desative a conta em vez de excluir.",
                "usuario_possui_vinculos");
        }

        IdentityResult resultado;
        try
        {
            resultado = await userManager.DeleteAsync(usuario);
        }
        catch (DbUpdateException)
        {
            throw new CrmBusinessException(
                "Não é possível excluir este usuário porque existem registros vinculados a ele. Desative a conta em vez de excluir.",
                "usuario_possui_vinculos");
        }

        if (!resultado.Succeeded)
        {
            throw new CrmBusinessException(IdentityErrors.Traduzir(resultado), "usuario_invalido");
        }

        await audit.RegistrarAsync("UsuarioExcluido", nameof(ApplicationUser), usuario.Id, new { usuario.NomeCompleto, usuario.Email }, ct);
    }

    // --- auxiliares ---

    private async Task<Guid?> ObterRegionalAtualAsync(CancellationToken ct) =>
        await db.Users.AsNoTracking().Where(u => u.Id == currentUser.UserId).Select(u => u.RegionalId).FirstOrDefaultAsync(ct);

    private async Task<Guid?> ValidarGrupoAsync(Guid? grupoId, Guid? regionalId, CancellationToken ct)
    {
        if (!grupoId.HasValue) return null;

        var grupo = await db.CrmGrupos.AsNoTracking().FirstOrDefaultAsync(g => g.Id == grupoId, ct)
            ?? throw new CrmNotFoundException("Grupo", grupoId.Value);

        if (regionalId is null || grupo.RegionalId != regionalId)
        {
            throw new CrmBusinessException("O grupo selecionado não pertence à regional deste usuário.", "grupo_regional_invalida");
        }

        return grupoId;
    }

    private async Task<ApplicationUser> CarregarComEscopoAsync(Guid id, CancellationToken ct)
    {
        var usuario = await db.Users.Include(u => u.Regional).Include(u => u.GestorComercial).Include(u => u.Grupo)
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new CrmNotFoundException("Usuário", id);

        if (!currentUser.TemVisaoTotal)
        {
            var regionalAtual = await ObterRegionalAtualAsync(ct);
            if (regionalAtual is null || usuario.RegionalId != regionalAtual)
            {
                throw new CrmForbiddenException("Você não tem permissão para acessar este usuário.");
            }
        }

        return usuario;
    }

    private async Task<UserSummaryDto> ParaDtoAsync(ApplicationUser usuario, CancellationToken ct)
    {
        var papeis = await userManager.GetRolesAsync(usuario);
        return new UserSummaryDto(
            usuario.Id, usuario.NomeCompleto, usuario.Email!, usuario.PhoneNumber,
            papeis.ToList(), usuario.RegionalId, usuario.Regional?.Nome,
            usuario.GestorComercialId, usuario.GestorComercial?.NomeCompleto,
            usuario.GrupoId, usuario.Grupo?.Nome,
            usuario.Ativo, usuario.LimiteMensalLeads, usuario.FotoUrl, usuario.CriadoEm, FiltroOQue.Separar(usuario.RecebeSomenteOQue));
    }
}
