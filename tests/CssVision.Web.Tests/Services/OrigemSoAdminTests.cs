using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>O campo Origem do lead é só para administradores (Admin/GestorMaster).</summary>
public class OrigemSoAdminTests
{
    private static LeadService Service(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

    private static async Task<(ApplicationDbContext Db, Guid AdminId, Guid ConsultorId, CrmLead Lead)> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultor.Id, Origem = "Meta ads" };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        return (db, admin.Id, consultor.Id, lead);
    }

    [Fact]
    public async Task Consultor_NaoRecebeAOrigem_NoDetalheNemNaLista()
    {
        using var factory = new TestDbContextFactory();
        var (db, _, consultorId, lead) = await PrepararAsync(factory);
        var service = Service(db, TestDbContextFactory.MockCurrentUser(consultorId).Object);

        Assert.Null((await service.ObterPorIdAsync(lead.Id, CancellationToken.None)).Origem);
        Assert.Null(Assert.Single((await service.ListarAsync(new LeadFilterRequest(), CancellationToken.None)).Itens).Origem);
    }

    [Fact]
    public async Task Consultor_NaoConsegueFiltrarPorOrigem()
    {
        using var factory = new TestDbContextFactory();
        var (db, _, consultorId, _) = await PrepararAsync(factory);
        var service = Service(db, TestDbContextFactory.MockCurrentUser(consultorId).Object);

        // Filtro por uma origem inexistente seria vazio; para o consultor o filtro é ignorado.
        var resultado = await service.ListarAsync(new LeadFilterRequest { Origem = "Outra" }, CancellationToken.None);

        Assert.Single(resultado.Itens);
    }

    [Fact]
    public async Task Administrador_RecebeAOrigem()
    {
        using var factory = new TestDbContextFactory();
        var (db, adminId, _, lead) = await PrepararAsync(factory);
        var service = Service(db, TestDbContextFactory.MockCurrentUser(adminId, visaoTotal: true).Object);

        Assert.Equal("Meta ads", (await service.ObterPorIdAsync(lead.Id, CancellationToken.None)).Origem);
    }

    [Fact]
    public async Task Consultor_AoEditarOLead_NaoApagaAOrigem()
    {
        using var factory = new TestDbContextFactory();
        var (db, _, consultorId, lead) = await PrepararAsync(factory);
        var service = Service(db, TestDbContextFactory.MockCurrentUser(consultorId).Object);

        // O formulário do consultor não tem o campo, então manda Origem nula.
        await service.AtualizarAsync(lead.Id, new LeadUpdateRequest(
            "Cliente editado", TipoPessoa.Fisica, null, null, null, null, null, null, null, null, null,
            Origem: null, null, null, null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, false, null, lead.RowVersion), CancellationToken.None);

        var salvo = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == lead.Id);
        Assert.Equal("Cliente editado", salvo.NomeOuRazaoSocial);
        Assert.Equal("Meta ads", salvo.Origem);
    }
}
