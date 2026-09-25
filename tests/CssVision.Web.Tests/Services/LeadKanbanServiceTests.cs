using Microsoft.EntityFrameworkCore;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class LeadKanbanServiceTests
{
    [Fact]
    public async Task ObterBoardAsync_SeparaLeadsManuaisDeAutomaticos_PorPadraoMostraSoManuais()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");

        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Lead Manual", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, CriadoManualmente = true },
            new CrmLead { NomeOuRazaoSocial = "Lead Meta Ads", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, Origem = "Meta ads", CriadoManualmente = false }
        );
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var boardManual = await service.ObterBoardAsync(new LeadKanbanFilterRequest { CriadoManualmente = true }, CancellationToken.None);
        var boardAutomatico = await service.ObterBoardAsync(new LeadKanbanFilterRequest { CriadoManualmente = false }, CancellationToken.None);

        var cartaoManual = Assert.Single(boardManual.Colunas.SelectMany(c => c.Cartoes));
        Assert.Equal("Lead Manual", cartaoManual.NomeOuRazaoSocial);

        var cartaoAutomatico = Assert.Single(boardAutomatico.Colunas.SelectMany(c => c.Cartoes));
        Assert.Equal("Lead Meta Ads", cartaoAutomatico.NomeOuRazaoSocial);
    }

    [Fact]
    public async Task ObterBoardAsync_LeadSemCriadoManualmenteExplicito_CaiNoGrupoManualPorPadrao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");

        db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "Lead Antigo", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var board = await service.ObterBoardAsync(new LeadKanbanFilterRequest(), CancellationToken.None);

        Assert.Single(board.Colunas.SelectMany(c => c.Cartoes));
    }

    [Fact]
    public async Task ObterBoardAsync_CartaoMostraPlacaDoLead_OuDoVeiculoDaOportunidade()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var etapaPipeline = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);

        var comPlaca = new CrmLead { NomeOuRazaoSocial = "Com placa", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, Placa = "ABC1D23" };
        var soNoVeiculo = new CrmLead { NomeOuRazaoSocial = "Placa no veículo", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        db.CrmLeads.AddRange(comPlaca, soNoVeiculo);
        var oportunidade = new CrmOpportunity { LeadId = soNoVeiculo.Id, Titulo = "AGV", ResponsavelId = vendedor.Id, EtapaId = etapaPipeline.Id };
        oportunidade.Veiculo = new CrmVeiculo { OpportunityId = oportunidade.Id, Placa = "XYZ9K87" };
        db.CrmOpportunities.Add(oportunidade);
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var cartoes = (await service.ObterBoardAsync(new LeadKanbanFilterRequest(), CancellationToken.None))
            .Colunas.SelectMany(c => c.Cartoes).ToDictionary(c => c.NomeOuRazaoSocial);

        Assert.Equal("ABC1D23", cartoes["Com placa"].Placa);
        Assert.Equal("XYZ9K87", cartoes["Placa no veículo"].Placa);
    }

    [Fact]
    public async Task ObterBoardAsync_CartaoTrazOOQue_ParaQualquerUsuario()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, ProdutoInteresse = "AGV TRUCK" });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var cartao = Assert.Single((await service.ObterBoardAsync(new LeadKanbanFilterRequest(), CancellationToken.None)).Colunas.SelectMany(c => c.Cartoes));
        Assert.Equal("AGV TRUCK", cartao.OQue);
    }

    [Fact]
    public async Task ObterBoardAsync_CategoriaMigracao_UsaOMarcadorDaMigracao_NaoOTextoDaOrigem()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        db.CrmLeads.AddRange(
            // Migrado com tag de campanha: a Origem virou "UGC", mas continua sendo da migração.
            new CrmLead { NomeOuRazaoSocial = "Migrado", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id,
                Origem = "UGC", ConsentimentoOrigem = OrigemLead.MarcadorMigracaoNotion },
            new CrmLead { NomeOuRazaoSocial = "Meta", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, Origem = "UGC" });
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var board = await service.ObterBoardAsync(new LeadKanbanFilterRequest { Categoria = ["Migração"] }, CancellationToken.None);

        Assert.Equal("Migrado", Assert.Single(board.Colunas.SelectMany(c => c.Cartoes)).NomeOuRazaoSocial);
    }

    [Theory]
    [InlineData("UGC", "UGC")]
    [InlineData("Demand gen", "Demand Gen")]
    [InlineData(" lookalike ", "Lookalike")]
    [InlineData("Campanha desconhecida", null)]
    [InlineData(null, null)]
    public void OrigemLead_TagDaCampanha_PadronizaAsTags(string? campanha, string? esperado)
    {
        Assert.Equal(esperado, OrigemLead.TagDaCampanha(campanha));
    }

    [Theory]
    [InlineData("TrafegoPago", new[] { "Meta", "Site" })]
    [InlineData("Notion", new[] { "Migrado", "Sincronizado" })]
    [InlineData(null, new[] { "Manual", "Meta", "Migrado", "Site", "Sincronizado" })]
    public async Task ObterBoardAsync_FiltroFonte_SeparaTrafegoPagoDeNotion(string? fonte, string[] esperados)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        CrmLead Lead(string nome) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id };
        var meta = Lead("Meta"); meta.MetaLeadId = "123";
        var site = Lead("Site"); site.ConsentimentoOrigem = OrigemLead.MarcadorFormularioSite;
        // Card migrado do Notion que trouxe o ID do lead no Meta: continua sendo do Notion.
        var migrado = Lead("Migrado"); migrado.ConsentimentoOrigem = OrigemLead.MarcadorMigracaoNotion; migrado.MetaLeadId = "999";
        var sincronizado = Lead("Sincronizado"); sincronizado.ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion;
        db.CrmLeads.AddRange(meta, site, migrado, sincronizado, Lead("Manual"));
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var nomes = (await service.ObterBoardAsync(new LeadKanbanFilterRequest { Fonte = fonte is null ? null : [fonte] }, CancellationToken.None))
            .Colunas.SelectMany(c => c.Cartoes).Select(c => c.NomeOuRazaoSocial).OrderBy(n => n).ToArray();

        Assert.Equal(esperados.OrderBy(n => n).ToArray(), nomes);
    }

    [Fact]
    public async Task ObterBoardAsync_TrazSoAPrimeiraPaginaPorColuna_ComOTotalDaColuna()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var inicio = DateTimeOffset.UtcNow.AddDays(-10);
        for (var i = 0; i < 7; i++)
        {
            db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = $"Lead {i}", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id });
        }
        await db.SaveChangesAsync();
        // Data de chegada distinta por lead: "Lead 6" é o mais recente.
        var todos = await db.CrmLeads.ToListAsync();
        foreach (var lead in todos) lead.CriadoEm = inicio.AddHours(int.Parse(lead.NomeOuRazaoSocial[5..]));
        await db.SaveChangesAsync();

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadKanbanService(db, new EquipeComercialService(db, currentUser.Object));

        var semEtapa = (await service.ObterBoardAsync(new LeadKanbanFilterRequest { CartoesPorColuna = 3 }, CancellationToken.None)).Colunas[0];
        Assert.Equal(7, semEtapa.Total);
        Assert.Equal(["Lead 6", "Lead 5", "Lead 4"], semEtapa.Cartoes.Select(c => c.NomeOuRazaoSocial));

        // "Ver mais": próxima página da mesma coluna, sem repetir cartões.
        var proxima = await service.ObterCartoesAsync(new LeadKanbanColunaRequest { EtapaId = null, Pular = 3, Quantidade = 3 }, CancellationToken.None);
        Assert.Equal(["Lead 3", "Lead 2", "Lead 1"], proxima.Select(c => c.NomeOuRazaoSocial));
    }
}
