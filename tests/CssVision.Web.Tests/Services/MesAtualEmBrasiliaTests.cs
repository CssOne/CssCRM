using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Os painéis e o ranking mostram o mês atual de Brasília — não os "últimos 30 dias" nem o mês em UTC.</summary>
public class MesAtualEmBrasiliaTests
{
    [Fact]
    public void Brasilia_ViraODiaAMeiaNoiteDeBrasilia_NaoDoUtc()
    {
        // 02:30 UTC de 1º de outubro ainda é 23:30 de 30 de setembro em Brasília.
        var antes = new DateTimeOffset(2026, 10, 1, 2, 30, 0, TimeSpan.Zero);
        Assert.Equal(new DateOnly(2026, 9, 30), HorarioBrasilia.Dia(antes));
        // 03:00 UTC já é meia-noite do dia 1º.
        Assert.Equal(new DateOnly(2026, 10, 1), HorarioBrasilia.Dia(antes.AddMinutes(30)));

        Assert.Equal(new DateTimeOffset(2026, 10, 1, 3, 0, 0, TimeSpan.Zero), HorarioBrasilia.Inicio(new DateOnly(2026, 10, 1)));
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 3, 0, 0, TimeSpan.Zero).AddTicks(-1), HorarioBrasilia.Fim(new DateOnly(2026, 10, 31)));
    }

    [Fact]
    public async Task RankingDaGestao_SemPeriodo_SoTemOMesAtual()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);

        CrmLead Lead(string nome, Guid resp) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = resp };
        var l1 = Lead("Do mês", ana.Id);
        var l2 = Lead("Do mês passado", bruna.Id);
        db.CrmLeads.AddRange(l1, l2);
        await db.SaveChangesAsync();
        var agora = DateTimeOffset.UtcNow;
        db.CrmOpportunities.AddRange(
            new CrmOpportunity { LeadId = l1.Id, Titulo = "V1", ResponsavelId = ana.Id, EtapaId = ganho.Id, DataEfetivaFechamento = agora, ValorFinal = 100m },
            // Último instante do mês passado: fora do mês atual.
            new CrmOpportunity { LeadId = l2.Id, Titulo = "V2", ResponsavelId = bruna.Id, EtapaId = ganho.Id,
                DataEfetivaFechamento = HorarioBrasilia.Inicio(HorarioBrasilia.PrimeiroDiaDoMes(HorarioBrasilia.Hoje).AddDays(-1)), ValorFinal = 900m });
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var gestao = new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db));

        var resumo = await gestao.ObterResumoAsync(null, null, CancellationToken.None);

        var primeira = resumo.Ranking.First();
        Assert.Equal(ana.Id, primeira.VendedorId);
        Assert.Equal(100m, primeira.ValorGanho);
        Assert.Equal(0m, resumo.Ranking.Single(r => r.VendedorId == bruna.Id).ValorGanho); // a venda dela foi no mês passado
    }
}
