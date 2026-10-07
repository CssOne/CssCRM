using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Etapa 1 da volta da MG134: desarquiva só o que o sistema arquivou nas migrations de 04 e 05/10, uma vez.</summary>
public class ReativarLeadsMg134Tests
{
    private static CrmLead Lead(string nome, string regional, bool arquivado, DateTimeOffset? arquivadoEm, Guid? arquivadoPorId = null) => new()
    {
        NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, Regional = regional,
        Arquivado = arquivado, ArquivadoEm = arquivadoEm, ArquivadoPorId = arquivadoPorId,
    };

    private static async Task<bool> ArquivadoAsync(ApplicationDbContext db, string nome) =>
        (await db.CrmLeads.AsNoTracking().SingleAsync(l => l.NomeOuRazaoSocial == nome)).Arquivado;

    [Fact]
    public async Task DesarquivaSoOQueOSistemaArquivouNaMG134_ENaoMexeNoResto()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var pessoa = await factory.CriarUsuarioAsync(db, "Admin");
        var dia5 = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        db.CrmLeads.AddRange(
            Lead("sistema-134", "MG134", true, dia5),
            Lead("excluido-por-pessoa", "MG134", true, dia5, pessoa.Id),
            Lead("arquivado-antigo", "MG134", true, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            Lead("outra-regional", "MG132", true, dia5),
            Lead("ja-ativo", "MG134", false, null));
        await db.SaveChangesAsync();

        var reativados = await ReativarLeadsMg134BackgroundService.ExecutarAsync(db, CancellationToken.None);

        Assert.Equal(1, reativados);
        db.ChangeTracker.Clear();
        Assert.False(await ArquivadoAsync(db, "sistema-134"));
        Assert.Null((await db.CrmLeads.AsNoTracking().SingleAsync(l => l.NomeOuRazaoSocial == "sistema-134")).ArquivadoEm);
        Assert.True(await ArquivadoAsync(db, "excluido-por-pessoa"));   // exclusão feita por uma pessoa continua
        Assert.True(await ArquivadoAsync(db, "arquivado-antigo"));       // arquivado antes de 04/10 não é da migration
        Assert.True(await ArquivadoAsync(db, "outra-regional"));         // só a MG134
        Assert.False(await ArquivadoAsync(db, "ja-ativo"));
    }

    [Fact]
    public async Task RodaUmaVezSo_ExclusaoDepoisDaReativacaoNaoVoltaNaProximaInicializacao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var dia5 = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        db.CrmLeads.Add(Lead("sistema-134", "MG134", true, dia5));
        await db.SaveChangesAsync();

        Assert.Equal(1, await ReativarLeadsMg134BackgroundService.ExecutarAsync(db, CancellationToken.None));
        Assert.True(await db.CrmParametros.AnyAsync(p => p.Chave == ReativarLeadsMg134BackgroundService.ChaveExecutado));

        // Algo é arquivado pelo sistema depois (mesmo critério) e a app reinicia: não pode voltar.
        db.ChangeTracker.Clear();
        var lead = await db.CrmLeads.SingleAsync(l => l.NomeOuRazaoSocial == "sistema-134");
        lead.Arquivado = true;
        lead.ArquivadoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();

        Assert.Null(await ReativarLeadsMg134BackgroundService.ExecutarAsync(db, CancellationToken.None));
        db.ChangeTracker.Clear();
        Assert.True(await ArquivadoAsync(db, "sistema-134"));
    }
}
