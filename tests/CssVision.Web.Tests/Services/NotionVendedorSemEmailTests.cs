using System.Text.Json;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Cards cujo "Vendedor" não traz e-mail no Notion não caem em "Vendedor não identificado".</summary>
public class NotionVendedorSemEmailTests
{
    private static JsonElement Pagina(string pageId, string nome, string? vendedorNotionId, string? vendedorNome = "Maria Vendedora", string? email = null, string tipo = "person")
    {
        var props = new Dictionary<string, object>
        {
            ["Name"] = new { type = "title", title = new object[] { new { plain_text = nome } } },
            ["WhatsApp"] = new { type = "rich_text", rich_text = new object[] { new { plain_text = "31988887777" } } },
            ["Status"] = new { type = "select", select = new { name = "EM ATENDIMENTO" } },
        };
        if (vendedorNotionId is not null)
        {
            object pessoa = email is null && vendedorNome is null
                ? new { id = vendedorNotionId, type = tipo }
                : email is null
                ? new { id = vendedorNotionId, type = tipo, name = vendedorNome }
                : new { id = vendedorNotionId, type = tipo, name = vendedorNome, person = new { email } };
            props["Vendedor"] = new { type = "people", people = new[] { pessoa } };
        }
        return JsonDocument.Parse(JsonSerializer.Serialize(new { id = pageId, properties = props })).RootElement.Clone();
    }

    private static async Task<(ApplicationDbContext Db, NotionSyncService Service, Guid Regional, Guid Ganho, ApplicationUser Placeholder, Dictionary<string, Guid> Etapas)> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var regional = new CrmRegional { Nome = "MG132" };
        db.CrmRegionais.Add(regional);
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 1, TipoEtapaPipeline.Ganho);
        var etapa = await factory.ObterOuCriarEtapaLeadAsync(db, "Em atendimento (Leads)", 1);
        var placeholder = await factory.CriarUsuarioAsync(db, "Vendedor não identificado (MG132)");
        placeholder.Email = "vendedor.nao.identificado.mg132@cssvision.local";
        placeholder.NormalizedEmail = placeholder.Email.ToUpperInvariant();
        placeholder.Ativo = false;
        await db.SaveChangesAsync();
        var service = new NotionSyncService(db, TestDbContextFactory.CreateUserManager(db), NullLogger<NotionSyncService>.Instance);
        return (db, service, regional.Id, ganho.Id, placeholder, new Dictionary<string, Guid> { ["Em atendimento (Leads)"] = etapa.Id });
    }

    [Fact]
    public async Task VendedorSemEmail_GanhaLoginInativoELeadVaiParaEle_ComOMesmoIdNaoDuplica()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;
        var notionId = "56ded1ab-edda-4365-862f-e2d90844933a";

        await service.ProcessarPaginaAsync(Pagina("p1", "Cliente 1", notionId), regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);
        db.ChangeTracker.Clear();
        await service.ProcessarPaginaAsync(Pagina("p2", "Cliente 2", notionId), regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);

        var vendedora = await db.Users.SingleAsync(u => u.NotionUserId == notionId);
        Assert.Equal("Maria Vendedora", vendedora.NomeCompleto);
        Assert.Equal($"notion.{notionId}@cssvision.local", vendedora.Email);
        Assert.False(vendedora.Ativo); // um administrador ativa e define a senha
        Assert.False(vendedora.RecebeLeads); // e nunca entra no rodízio do tráfego sozinha
        Assert.Equal(vendedora.RegionalId, regional);
        Assert.All(await db.CrmLeads.ToListAsync(), l => Assert.Equal(vendedora.Id, l.ResponsavelId));
    }

    [Fact]
    public async Task LeadParadoNoPlaceholder_VaiParaOVendedorQuandoOCardEReprocessado()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;
        var existente = await factory.CriarUsuarioAsync(db, "Thais");
        existente.NotionUserId = "abc-123";
        existente.Ativo = false; // vendedora real, mas inativa
        db.CrmLeads.Add(new CrmLead
        {
            NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, NotionPageId = "p1", ResponsavelId = placeholder.Id,
            NotionVendedorEmail = NotionSyncService.SemVendedor, ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion,
        });
        await db.SaveChangesAsync();

        await service.ProcessarPaginaAsync(Pagina("p1", "Cliente", "abc-123", "Thais"), regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);

        Assert.Equal(existente.Id, (await db.CrmLeads.SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task VendedorComEmailSemUsuario_GanhaLoginComEmailEIdVinculado_ELoginExistenteRecebeOId()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;
        var jaExiste = await factory.CriarUsuarioAsync(db, "Ana");

        await service.ProcessarPaginaAsync(Pagina("p1", "C1", "id-novo", "Nova Pessoa", email: "nova@teste.com"), regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);
        db.ChangeTracker.Clear();
        await service.ProcessarPaginaAsync(Pagina("p2", "C2", "id-ana", "Ana", email: jaExiste.Email), regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);

        var nova = await db.Users.SingleAsync(u => u.Email == "nova@teste.com");
        Assert.Equal("id-novo", nova.NotionUserId);
        Assert.False(nova.Ativo);
        Assert.Equal("id-ana", (await db.Users.SingleAsync(u => u.Id == jaExiste.Id)).NotionUserId);
    }

    [Fact]
    public async Task VendedorSemNomeNoNotion_GanhaLoginProvisorio_EOLeadSaiDoPlaceholder()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;
        var notionId = "56ded1ab-edda-4365-862f-e2d90844933a";
        // Lead que já estava parado no "não identificado" e é reprocessado.
        db.CrmLeads.Add(new CrmLead
        {
            NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, NotionPageId = "p1", ResponsavelId = placeholder.Id,
            NotionVendedorEmail = NotionSyncService.SemVendedor, ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion,
        });
        await db.SaveChangesAsync();

        // Pessoa sem nome (a integração do Notion não a enxerga): só o id.
        await service.ProcessarPaginaAsync(Pagina("p1", "Cliente", notionId, vendedorNome: null), regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);

        var vendedor = await db.Users.SingleAsync(u => u.NotionUserId == notionId);
        Assert.Equal("Vendedor Notion 56ded1ab-933a", vendedor.NomeCompleto); // início + fim do id (vários ids dividem o início)
        Assert.False(vendedor.Ativo);
        Assert.Equal(vendedor.Id, (await db.CrmLeads.SingleAsync()).ResponsavelId);
    }

    [Fact]
    public async Task PorcentagemDoNotion_EGuardadaEmPontos()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;
        var props = new Dictionary<string, object>
        {
            ["Name"] = new { type = "title", title = new object[] { new { plain_text = "Cliente venda" } } },
            ["WhatsApp"] = new { type = "rich_text", rich_text = new object[] { new { plain_text = "31988887777" } } },
            ["Status"] = new { type = "select", select = new { name = "VENDA CONCLUIDA" } },
            ["Mensalidade"] = new { type = "number", number = 244.99 },
            ["Porcentagem"] = new { type = "number", number = 0.23 }, // 23% no Notion
        };
        var pagina = JsonDocument.Parse(JsonSerializer.Serialize(new { id = "venda-1", properties = props })).RootElement.Clone();

        await service.ProcessarPaginaAsync(pagina, regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);

        Assert.Equal(23m, (await db.CrmOpportunities.SingleAsync()).Porcentagem);
    }

    [Fact]
    public async Task BotOuCardSemVendedor_ContinuamNoPlaceholder()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;

        await service.ProcessarPaginaAsync(Pagina("p1", "Do bot", "bot-1", "Make", tipo: "bot"), regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);
        db.ChangeTracker.Clear();
        await service.ProcessarPaginaAsync(Pagina("p2", "Sem vendedor", null), regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);

        Assert.All(await db.CrmLeads.ToListAsync(), l => Assert.Equal(placeholder.Id, l.ResponsavelId));
        Assert.DoesNotContain(await db.Users.ToListAsync(), u => u.NotionUserId == "bot-1");
    }

    [Fact]
    public async Task EmailAntigoDeContaUnificada_LigaOCardAContaQueFicou_SemCriarOutra()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, regional, ganho, placeholder, etapas) = await PrepararAsync(factory);
        await using var _ = db;
        var queFicou = await factory.CriarUsuarioAsync(db, "Carol Barcelos");
        db.CrmUsuarioAliases.Add(new CrmUsuarioAlias { EmailNormalizado = "CAROLBARCELOSAGV@GMAIL.COM", UsuarioId = queFicou.Id });
        await db.SaveChangesAsync();
        var usuariosAntes = await db.Users.CountAsync();

        await service.ProcessarPaginaAsync(Pagina("p1", "Cliente", "id-carol", "Carol Barcelos", email: "carolbarcelosagv@gmail.com"), regional, "MG132", etapas, ganho, placeholder.Id, false, CancellationToken.None);

        Assert.Equal(queFicou.Id, (await db.CrmLeads.SingleAsync()).ResponsavelId);
        Assert.Equal(usuariosAntes, await db.Users.CountAsync()); // nenhuma conta nova
        Assert.Equal("id-carol", (await db.Users.SingleAsync(u => u.Id == queFicou.Id)).NotionUserId);
    }

    [Fact]
    public async Task CardDeVendedorSemNome_FicaDeForaSalvoSeOUsuarioJaFoiRenomeado()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, _, _, _, _) = await PrepararAsync(factory);
        await using var _ = db;
        var renomeado = await factory.CriarUsuarioAsync(db, "Fulano Renomeado");
        renomeado.NotionUserId = "id-renomeado";
        var provisorio = await factory.CriarUsuarioAsync(db, NotionPageExtensions.NomeProvisorio("id-provisorio-0000-1111"));
        provisorio.NotionUserId = "id-provisorio-0000-1111";
        await db.SaveChangesAsync();

        // Sem nome no Notion (só o id) e sem usuário: fica de fora (nem lead nem login novo).
        Assert.True(await service.VendedorSemNomeNaoRevisadoAsync(Pagina("p1", "C", "id-desconhecido-0000", vendedorNome: null), CancellationToken.None));
        // Usuário ainda com o nome provisório: também de fora.
        Assert.True(await service.VendedorSemNomeNaoRevisadoAsync(Pagina("p2", "C", "id-provisorio-0000-1111", vendedorNome: null), CancellationToken.None));
        // Usuário já renomeado em Usuários: o card entra normal.
        Assert.False(await service.VendedorSemNomeNaoRevisadoAsync(Pagina("p3", "C", "id-renomeado", vendedorNome: null), CancellationToken.None));
        // Vendedor com nome, ou card sem vendedor: não é o caso.
        Assert.False(await service.VendedorSemNomeNaoRevisadoAsync(Pagina("p4", "C", "qualquer", "Maria"), CancellationToken.None));
        Assert.False(await service.VendedorSemNomeNaoRevisadoAsync(Pagina("p5", "C", null), CancellationToken.None));
        Assert.Empty(await db.Users.Where(u => u.NotionUserId == "id-desconhecido-0000").ToListAsync()); // não criou login
    }
}
