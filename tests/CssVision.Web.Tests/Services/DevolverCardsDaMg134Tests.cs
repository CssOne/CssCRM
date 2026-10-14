using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Correção pontual: card da MG134 arquivado pelo sistema volta ao vendedor do Notion, sem passar por cima de exclusão feita por pessoa.</summary>
public class DevolverCardsDaMg134Tests
{
    private static CrmLead Lead(string nome, Guid responsavelId, bool arquivado, Guid? arquivadoPorId = null, string? email = null) => new()
    {
        NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, Regional = "MG134", ResponsavelId = responsavelId,
        Arquivado = arquivado, ArquivadoEm = arquivado ? DateTimeOffset.UtcNow : null, ArquivadoPorId = arquivadoPorId,
        Email = email, EmailNormalizado = email,
    };

    [Fact]
    public async Task DesarquivaEEntregaAoConsultor_QuandoArquivadoPeloSistemaComVendedorProvisorio()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var provisorio = await factory.CriarUsuarioAsync(db, "Vendedor não identificado (MG134)");
        var nayrana = await factory.CriarUsuarioAsync(db, "Nayrana Abreu");
        var lead = Lead("Jose", provisorio.Id, arquivado: true);
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();

        var resultado = await DevolverCardsDaMg134AoVendedorBackgroundService.DevolverAsync(db, lead.Id, "Nayrana Abreu", CancellationToken.None);

        Assert.Contains("desarquivado", resultado);
        db.ChangeTracker.Clear();
        var depois = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == lead.Id);
        Assert.False(depois.Arquivado);
        Assert.Null(depois.ArquivadoEm);
        Assert.Equal(nayrana.Id, depois.ResponsavelId);
    }

    [Fact]
    public async Task NaoMexe_QuandoUmaPessoaExcluiuOuOutroLeadAtivoUsaOMesmoEmail()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var provisorio = await factory.CriarUsuarioAsync(db, "Vendedor não identificado (MG134)");
        var nayrana = await factory.CriarUsuarioAsync(db, "Nayrana Abreu");
        var excluido = Lead("Excluido", provisorio.Id, arquivado: true, arquivadoPorId: nayrana.Id);
        var repetido = Lead("Repetido", provisorio.Id, arquivado: true, email: "a@x.com");
        var ativo = Lead("Ativo", nayrana.Id, arquivado: false, email: "a@x.com");
        db.CrmLeads.AddRange(excluido, repetido, ativo);
        await db.SaveChangesAsync();

        Assert.Contains("pessoa", await DevolverCardsDaMg134AoVendedorBackgroundService.DevolverAsync(db, excluido.Id, "Nayrana Abreu", CancellationToken.None));
        Assert.Contains("e-mail/CPF", await DevolverCardsDaMg134AoVendedorBackgroundService.DevolverAsync(db, repetido.Id, "Nayrana Abreu", CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.True((await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == excluido.Id)).Arquivado);
        Assert.True((await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == repetido.Id)).Arquivado);
    }
}
