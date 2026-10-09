using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Cliente novo ou editado exige telefone, cidade, estado e placa; a regional vem do consultor responsável.</summary>
public class CadastroDeClienteObrigatorioTests
{
    private static LeadCreateRequest Novo(string? telefone = "31999990000", string? cidade = "Belo Horizonte", string? estado = "MG", string? placa = "ABC1D23", string? regional = "Outra") =>
        new("Cliente", TipoPessoa.Fisica, null, telefone, null, null, null, null, cidade, estado, regional, null, null, null, placa, null, null, null,
            null, null, null, null, null, null, null, null, null, null, null, null, true, null);

    private static async Task<(LeadService Servico, TestDbContextFactory Factory, CssVision.Web.Data.ApplicationDbContext Db, Guid ConsultorId)> MontarAsync()
    {
        var factory = new TestDbContextFactory();
        var db = factory.CreateContext();
        var regional = await factory.CriarRegionalAsync(db, "MG132");
        var consultor = await factory.CriarUsuarioAsync(db, "Consultora");
        consultor.RegionalId = regional.Id;
        await db.SaveChangesAsync();
        var usuario = TestDbContextFactory.MockCurrentUser(consultor.Id);
        var servico = new LeadService(db, usuario.Object, new EquipeComercialService(db, usuario.Object),
            new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        return (servico, factory, db, consultor.Id);
    }

    [Theory]
    [InlineData(null, "BH", "MG", "ABC1D23", "telefone_obrigatorio")]
    [InlineData("31999990000", " ", "MG", "ABC1D23", "cidade_obrigatoria")]
    [InlineData("31999990000", "BH", null, "ABC1D23", "estado_obrigatorio")]
    [InlineData("31999990000", "BH", "M", "ABC1D23", "estado_obrigatorio")]
    [InlineData("31999990000", "BH", "MG", " ", "placa_obrigatoria")]
    public async Task Criar_SemUmDosCamposObrigatorios_Recusa(string? tel, string? cidade, string? uf, string? placa, string codigo)
    {
        var (servico, factory, db, _) = await MontarAsync();
        using var _f = factory; await using var _d = db;

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.CriarAsync(Novo(tel, cidade, uf, placa), CancellationToken.None));
        Assert.Equal(codigo, erro.Codigo);
    }

    [Fact]
    public async Task Criar_UsaARegionalDoConsultorResponsavel_EIgnoraAInformada()
    {
        var (servico, factory, db, consultorId) = await MontarAsync();
        using var _f = factory; await using var _d = db;

        var resultado = await servico.CriarAsync(Novo(regional: "Outra"), CancellationToken.None);

        var lead = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == resultado.Lead!.Id);
        Assert.Equal(consultorId, lead.ResponsavelId);
        Assert.Equal("MG132", lead.Regional);
    }

    [Fact]
    public async Task Criar_ConsultorSemRegional_SemRegionalInformada_Recusa()
    {
        var (servico, factory, db, consultorId) = await MontarAsync();
        using var _f = factory; await using var _d = db;
        (await db.Users.FindAsync(consultorId))!.RegionalId = null;
        await db.SaveChangesAsync();

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.CriarAsync(Novo(regional: null), CancellationToken.None));
        Assert.Equal("regional_obrigatoria", erro.Codigo);
    }
}
