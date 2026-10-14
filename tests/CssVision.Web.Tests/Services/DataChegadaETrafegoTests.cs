using CssVision.Web.Authorization;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Data de chegada vinda de importação é mantida e a contagem de leads de tráfego do mês.</summary>
public class DataChegadaETrafegoTests
{
    [Fact]
    public async Task DataDeChegadaInformada_NaoESobrescritaAoSalvar()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var chegada = new DateTimeOffset(2025, 3, 10, 14, 30, 0, TimeSpan.Zero);
        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "Do Notion", TipoPessoa = TipoPessoa.Fisica, CriadoEm = chegada },
            new CrmLead { NomeOuRazaoSocial = "Novo", TipoPessoa = TipoPessoa.Fisica });
        await db.SaveChangesAsync();

        var leads = await db.CrmLeads.AsNoTracking().ToDictionaryAsync(l => l.NomeOuRazaoSocial, l => l.CriadoEm);
        Assert.Equal(chegada, leads["Do Notion"]);
        Assert.True(leads["Novo"] > DateTimeOffset.UtcNow.AddMinutes(-1)); // sem data informada: o momento da gravação
    }

    [Fact]
    public async Task GestaoComercial_ContaLeadsDeTrafegoDoMes_DoNotionEDoSistemaNovo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        var mesPassado = DateTimeOffset.UtcNow.AddMonths(-2);
        CrmLead Lead(string nome) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id };

        var meta = Lead("Meta"); meta.MetaLeadId = "1";
        var site = Lead("Site"); site.ConsentimentoOrigem = OrigemLead.MarcadorFormularioSite;
        var notionCampanha = Lead("Notion com campanha"); notionCampanha.ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion; notionCampanha.Campanha = "Lookalike";
        var notionGoogle = Lead("Notion do Google"); notionGoogle.ConsentimentoOrigem = OrigemLead.MarcadorMigracaoNotion; notionGoogle.Gclid = "abc";
        var notionIndicacao = Lead("Notion indicação"); notionIndicacao.ConsentimentoOrigem = OrigemLead.MarcadorMigracaoNotion;
        var manual = Lead("Cadastrado à mão");
        var antigo = Lead("Meta antigo"); antigo.MetaLeadId = "2"; antigo.CriadoEm = mesPassado;
        db.CrmLeads.AddRange(meta, site, notionCampanha, notionGoogle, notionIndicacao, manual, antigo);
        await db.SaveChangesAsync();
        // O contexto marca a atribuição como "agora" ao inserir; o lead antigo foi atribuído no mês passado.
        antigo.ResponsavelAtribuidoEm = mesPassado;
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var gestao = new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db));
        var vendedor = Assert.Single(await gestao.ObterVendedoresAsync(CancellationToken.None), v => v.Id == ana.Id);

        Assert.Equal(4, vendedor.LeadsTrafegoNoMes); // Meta, Site, Notion com campanha e Notion do Google
    }

    [Fact]
    public async Task LeadDeTrafegoAtribuidoAOutro_SaiDeQuemTinhaEEntraEmQuemRecebeu_NoRodizioNaGestaoENaTv()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, bruna, Roles.Comercial);
        ana.LimiteMensalLeads = 1;
        bruna.LimiteMensalLeads = 1;
        var mesPassado = DateTimeOffset.UtcNow.AddMonths(-2);

        // Lead do sistema que a Ana pegou no mês passado: não conta para ninguém neste mês.
        var doSistema = new CrmLead { NomeOuRazaoSocial = "Meta", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id, MetaLeadId = "1" };
        // Card do Notion (chegou no mês passado) com a Bruna: vale pela chegada, então também não conta neste mês.
        var doNotion = new CrmLead
        {
            NomeOuRazaoSocial = "Notion", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = bruna.Id, ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion, Campanha = "Camp",
        };
        db.CrmLeads.AddRange(doSistema, doNotion);
        await db.SaveChangesAsync();
        doSistema.CriadoEm = mesPassado; doSistema.ResponsavelAtribuidoEm = mesPassado;
        doNotion.CriadoEm = mesPassado;
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var gestao = new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db));
        var rodizio = new LeadAssignmentService(db);
        async Task<(int ana, int bruna)> TrafegoNoMes()
        {
            var v = await gestao.ObterVendedoresAsync(CancellationToken.None);
            return (v.Single(x => x.Id == ana.Id).LeadsTrafegoNoMes, v.Single(x => x.Id == bruna.Id).LeadsTrafegoNoMes);
        }
        Assert.Equal((0, 0), await TrafegoNoMes());
        Assert.True(await rodizio.PodeReceberAsync(ana.Id, CancellationToken.None));
        Assert.True(await rodizio.PodeReceberAsync(bruna.Id, CancellationToken.None));

        // O gestor passa o lead do sistema da Ana para a Bruna e o card do Notion da Bruna para a Ana.
        doSistema.ResponsavelId = bruna.Id;
        doNotion.ResponsavelId = ana.Id;
        await db.SaveChangesAsync();

        // Lead do sistema: entra para quem recebeu (Bruna), no mês em que foi atribuído; a Ana não tem mais. Notion: segue pela data de chegada (mês passado).
        Assert.Equal((0, 1), await TrafegoNoMes());
        Assert.True(await rodizio.PodeReceberAsync(ana.Id, CancellationToken.None));     // Ana: 0 de 1 no mês
        Assert.False(await rodizio.PodeReceberAsync(bruna.Id, CancellationToken.None));  // Bruna: bateu o limite com o lead recebido
    }
}
