using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Erros 500 do POST /api/public/leads em produção: (1) Meta Lead ID que já está num lead excluído
/// (arquivado) — a busca por duplicado ignora arquivados e tentava criar outro, batendo no índice
/// único; (2) estado com mais de 2 letras estourando a coluna.
/// </summary>
public class IntakeMetaLeadIdEEstadoTests
{
    private static PublicLeadCreateRequest Request(string? metaLeadId, string? estado = "MG", string email = "novo@site.com") =>
        new("Cliente Site", "31977776666", email, estado, null, null, null, null, null, null, null, metaLeadId, "Facebook ADS", null, "AGV", "meta-instant-1");

    [Fact]
    public async Task MetaLeadIdDeLeadArquivado_NaoCriaOutroLead_DevolveOExistente()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var excluido = new CrmLead
        {
            NomeOuRazaoSocial = "Excluído", TipoPessoa = TipoPessoa.Fisica, MetaLeadId = "meta-999",
            EmailNormalizado = "outro@site.com", Arquivado = true,
        };
        db.CrmLeads.Add(excluido);
        await db.SaveChangesAsync();

        var service = new PublicLeadIntakeService(db, new NoOpLeadAssignmentService(), NullLogger<PublicLeadIntakeService>.Instance);
        var resultado = await service.CriarAsync(Request("meta-999"), CancellationToken.None);

        Assert.Equal(excluido.Id, resultado.LeadId);
        Assert.Equal(1, await db.CrmLeads.CountAsync());
    }

    [Fact]
    public async Task MetaLeadIdNovo_CriaNormalmente()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();

        var service = new PublicLeadIntakeService(db, new NoOpLeadAssignmentService(), NullLogger<PublicLeadIntakeService>.Instance);
        var resultado = await service.CriarAsync(Request("meta-1"), CancellationToken.None);

        Assert.Equal("meta-1", (await db.CrmLeads.SingleAsync(l => l.Id == resultado.LeadId)).MetaLeadId);
    }

    [Theory]
    [InlineData("MG", "MG")]
    [InlineData("mg", "MG")]
    [InlineData("Minas Gerais", "MG")]
    [InlineData("MG - Minas Gerais", "MG")]
    [InlineData("São Paulo", "SP")]
    [InlineData("Mato Grosso do Sul", "MS")]
    [InlineData("Mato Grosso", "MT")]
    [InlineData("Paraná", "PR")]
    [InlineData("Distrito Federal (DF)", "DF")]
    [InlineData("não sei", null)]
    [InlineData("", null)]
    public async Task Estado_ViraSiglaDaUf_OuVazio(string estado, string? esperado)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();

        var service = new PublicLeadIntakeService(db, new NoOpLeadAssignmentService(), NullLogger<PublicLeadIntakeService>.Instance);
        var resultado = await service.CriarAsync(Request(null, estado), CancellationToken.None);

        Assert.Equal(esperado, (await db.CrmLeads.SingleAsync(l => l.Id == resultado.LeadId)).Estado);
    }
}
