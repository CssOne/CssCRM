using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Limite mensal só com tráfego pago, distribuição dos leads que ficaram sem responsável e aviso de novo lead.</summary>
public class DistribuicaoLeadsTests
{
    private static CrmLead Trafego(string nome, Guid? responsavelId = null) =>
        new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = responsavelId, MetaLeadId = Guid.NewGuid().ToString(), CriadoManualmente = false };

    [Fact]
    public async Task Limite_NaoContaLeadsDoNotionNemCriadosManualmente()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        ana.LimiteMensalLeads = 2;

        // Muitos leads migrados/sincronizados do Notion (inclusive com ID do Meta) e um manual: nenhum conta.
        for (var i = 0; i < 10; i++)
        {
            db.CrmLeads.Add(new CrmLead
            {
                NomeOuRazaoSocial = $"Notion {i}", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, MetaLeadId = $"n{i}",
                ConsentimentoOrigem = i % 2 == 0 ? OrigemLead.MarcadorMigracaoNotion : OrigemLead.MarcadorSincronizacaoNotion,
            });
        }
        db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "Manual", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id });
        db.CrmLeads.Add(Trafego("Meta 1", ana.Id));
        await db.SaveChangesAsync();

        var service = new LeadAssignmentService(db);
        Assert.True(await service.PodeReceberAsync(ana.Id, CancellationToken.None));
        Assert.Equal(ana.Id, await service.ProximoResponsavelAsync(CancellationToken.None));

        db.CrmLeads.Add(Trafego("Meta 2", ana.Id));
        await db.SaveChangesAsync();
        Assert.False(await service.PodeReceberAsync(ana.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DistribuirPendentes_EntregaSoOsLeadsDoTrafegoSemResponsavel_EmRodizio()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna Vendedora");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);

        db.CrmLeads.AddRange(Trafego("Jose"), Trafego("Renato"));
        var arquivado = Trafego("Excluído"); arquivado.Arquivado = true;
        var notion = new CrmLead { NomeOuRazaoSocial = "Do Notion", TipoPessoa = TipoPessoa.Fisica, ConsentimentoOrigem = OrigemLead.MarcadorMigracaoNotion };
        db.CrmLeads.AddRange(arquivado, notion);
        await db.SaveChangesAsync();

        var hub = new CrmEventHub();
        using var tela = hub.Assinar();
        var distribuidos = await new LeadAssignmentService(db, hub).DistribuirPendentesAsync(CancellationToken.None);

        Assert.Equal(2, distribuidos);
        var leads = await db.CrmLeads.AsNoTracking().ToDictionaryAsync(l => l.NomeOuRazaoSocial);
        Assert.Equal(new[] { ana.Id, bruna.Id }.Order(), new[] { leads["Jose"].ResponsavelId!.Value, leads["Renato"].ResponsavelId!.Value }.Order());
        Assert.NotNull(leads["Jose"].ResponsavelAtribuidoEm);
        Assert.Null(leads["Excluído"].ResponsavelId);
        Assert.Null(leads["Do Notion"].ResponsavelId);
        Assert.True(tela.Leitor.TryRead(out _));
    }

    [Fact]
    public async Task DistribuirPendentes_SemConsultorDisponivel_DeixaParaOProximoCiclo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        db.CrmLeads.Add(Trafego("Jose"));
        await db.SaveChangesAsync();

        Assert.Equal(0, await new LeadAssignmentService(db).DistribuirPendentesAsync(CancellationToken.None));
        Assert.Null((await db.CrmLeads.SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task NovosLeads_TrazOsQuePassaramASerDoUsuarioDepoisDoCursor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana Vendedora");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna Vendedora");
        db.CrmLeads.AddRange(Trafego("Antigo", ana.Id), Trafego("Da Bruna", bruna.Id));
        await db.SaveChangesAsync();

        var service = new LeadAssignmentService(db);
        var primeira = await service.NovosLeadsAsync(ana.Id, null, CancellationToken.None);
        Assert.Empty(primeira.Leads); // sem cursor: só marca o ponto de partida

        await Task.Delay(20);
        var novo = Trafego("Novo");
        db.CrmLeads.Add(novo);
        await db.SaveChangesAsync();
        novo.ResponsavelId = ana.Id; // distribuído depois de chegar
        await db.SaveChangesAsync();

        var segunda = await service.NovosLeadsAsync(ana.Id, primeira.Agora, CancellationToken.None);
        Assert.Equal(["Novo"], segunda.Leads.Select(l => l.Nome));

        var terceira = await service.NovosLeadsAsync(ana.Id, segunda.Agora, CancellationToken.None);
        Assert.Empty(terceira.Leads);
    }
}
