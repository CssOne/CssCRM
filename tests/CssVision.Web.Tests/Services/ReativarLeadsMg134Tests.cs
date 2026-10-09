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

    [Fact]
    public async Task SemNadaElegivel_NaoMarcaComoFeita_GravaODiagnostico_ETentaDeNovoNaProximaInicializacao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var pessoa = await factory.CriarUsuarioAsync(db, "Admin");
        db.CrmLeads.AddRange(
            Lead("por-pessoa", "MG134", true, new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), pessoa.Id),
            Lead("antigo", "MG134", true, new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero)),
            Lead("sem-data", "MG134", true, null));
        await db.SaveChangesAsync();

        Assert.Equal(0, await ReativarLeadsMg134BackgroundService.ExecutarAsync(db, CancellationToken.None));

        // Nada voltou: não há marcador, e o diagnóstico (chave "notion:", visível na tela de sincronização) explica o porquê.
        Assert.False(await db.CrmParametros.AnyAsync(p => p.Chave == ReativarLeadsMg134BackgroundService.ChaveExecutado));
        var status = (await db.CrmParametros.AsNoTracking().SingleAsync(p => p.Chave == ReativarLeadsMg134BackgroundService.ChaveStatus)).Valor;
        Assert.StartsWith("notion:", ReativarLeadsMg134BackgroundService.ChaveStatus);
        Assert.Contains("MG134 arquivados: 3", status);
        Assert.Contains("por pessoa: 1", status);
        Assert.Contains("antes de 04/10: 1", status);
        Assert.Contains("sem data: 1", status);
        Assert.Contains("desarquivados agora: 0", status);

        // Na próxima inicialização, um lead elegível aparece e então volta (e aí sim marca como feita).
        db.CrmLeads.Add(Lead("elegivel", "MG134", true, new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();
        Assert.Equal(1, await ReativarLeadsMg134BackgroundService.ExecutarAsync(db, CancellationToken.None));
        Assert.True(await db.CrmParametros.AnyAsync(p => p.Chave == ReativarLeadsMg134BackgroundService.ChaveExecutado));

        // Já feita: o diagnóstico mostra o marcador.
        Assert.Null(await ReativarLeadsMg134BackgroundService.ExecutarAsync(db, CancellationToken.None));
        var depois = (await db.CrmParametros.AsNoTracking().SingleAsync(p => p.Chave == ReativarLeadsMg134BackgroundService.ChaveStatus)).Valor;
        Assert.Contains("já executada antes", depois);
    }

    [Fact]
    public async Task EmailOuCpfJaUsadoPorLeadAtivo_OuRepetidoEntreArquivados_FicaArquivado_SemQuebrarOResto()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var dia5 = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
        CrmLead Com(string nome, bool arquivado, string? email = null, string? doc = null)
        {
            var l = Lead(nome, "MG134", arquivado, arquivado ? dia5 : null);
            l.EmailNormalizado = email; l.DocumentoNormalizado = doc;
            return l;
        }
        db.CrmLeads.AddRange(
            Com("ativo-novo-cadastro", false, email: "a@x.com"),             // cliente recadastrado enquanto o antigo estava arquivado
            Com("arquivado-mesmo-email-do-ativo", true, email: "a@x.com"),   // não pode voltar (índice único de e-mail entre ativos)
            Com("ativo-cpf", false, doc: "11122233344"),
            Com("arquivado-mesmo-cpf-do-ativo", true, doc: "11122233344"),   // idem para CPF
            Com("repetido-1", true, email: "r@x.com"),                        // dois arquivados com o mesmo e-mail: volta só um
            Com("repetido-2", true, email: "r@x.com"),
            Com("normal", true, email: "n@x.com"),
            Com("sem-dados", true));
        await db.SaveChangesAsync();

        var reativados = await ReativarLeadsMg134BackgroundService.ExecutarAsync(db, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.True(await ArquivadoAsync(db, "arquivado-mesmo-email-do-ativo"));
        Assert.True(await ArquivadoAsync(db, "arquivado-mesmo-cpf-do-ativo"));
        Assert.Equal(1, await db.CrmLeads.CountAsync(l => (l.NomeOuRazaoSocial == "repetido-1" || l.NomeOuRazaoSocial == "repetido-2") && !l.Arquivado));
        Assert.False(await ArquivadoAsync(db, "normal"));
        Assert.False(await ArquivadoAsync(db, "sem-dados"));
        Assert.Equal(3, reativados); // normal, sem-dados e um dos repetidos
        var status = (await db.CrmParametros.AsNoTracking().SingleAsync(p => p.Chave == ReativarLeadsMg134BackgroundService.ChaveStatus)).Valor;
        Assert.Contains("ficaram arquivados por e-mail/CPF repetido: 3", status);
    }
}
