using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class LeadBoasVindasEmailTests
{
    private sealed class EmailFalso(bool resultado = true) : IEmailSender
    {
        public List<(string Para, string Assunto, string Html)> Enviados { get; } = [];
        public Task<bool> EnviarAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, CancellationToken ct)
        {
            Enviados.Add((destinatarioEmail, assunto, corpoHtml));
            return Task.FromResult(resultado);
        }
        public Task<IReadOnlyList<bool>> EnviarLoteComContaAsync(ContaSmtp conta, string remetenteNome, IReadOnlyList<MensagemEmail> mensagens, CancellationToken ct) => throw new NotSupportedException();
        public Task ValidarContaAsync(ContaSmtp conta, CancellationToken ct) => throw new NotSupportedException();
    }

    private static LeadBoasVindasEmailService Criar(ApplicationDbContext db, EmailFalso email, bool ativo = true, string? baseUrl = "https://crm.exemplo.com.br") =>
        new(db, email, new DescadastroEmailTokens(new EphemeralDataProtectionProvider()),
            new ConfigurationBuilder().AddInMemoryCollection(baseUrl is null ? [] : new Dictionary<string, string?> { ["App:PublicBaseUrl"] = baseUrl }).Build(),
            Options.Create(new BoasVindasEmailOptions { Ativo = ativo }), null!, NullLogger<LeadBoasVindasEmailService>.Instance);

    private static async Task<(CrmLead Lead, Domain.Identity.ApplicationUser Consultor)> CriarCenarioAsync(
        TestDbContextFactory factory, ApplicationDbContext db, string email = "silas@gmail.com", string produto = "AGV", bool comConsultor = true)
    {
        var consultor = await factory.CriarUsuarioAsync(db, "Ana Consultora");
        consultor.PhoneNumber = "(31) 98888-7777";
        consultor.FotoUrl = "/uploads/avatars/ana.jpg";
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "silas Dias dos Santos.", TipoPessoa = TipoPessoa.Fisica, Email = email,
            ProdutoInteresse = produto, ResponsavelId = comConsultor ? consultor.Id : null,
        };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        return (lead, consultor);
    }

    [Fact]
    public void Montar_DeveTerSaudacaoCorreta_FotoDoConsultor_BotaoVerde_ELinkDescadastro()
    {
        var html = LeadBoasVindasEmail.Montar("silas Dias dos Santos.", "Ana Consultora", "https://crm.exemplo.com.br/uploads/ana.jpg",
            "https://wa.me/5531988887777?text=oi", "https://crm.exemplo.com.br/api/publico/descadastro?t=abc");

        Assert.Contains("Olá, Silas! Tudo bem?", html);
        Assert.Contains("Você vai ser atendido por <strong>Ana Consultora</strong>", html);
        Assert.Contains("src=\"https://crm.exemplo.com.br/uploads/ana.jpg\"", html);
        Assert.Contains("background:#25D366", html);
        Assert.Contains(">Fazer cotação</a>", html);
        Assert.Contains("href=\"https://wa.me/5531988887777?text=oi\"", html);
        Assert.Contains("Cancelar inscrição", html);
        Assert.Contains("Os benefícios estão sujeitos às condições de aceitação", html);
    }

    [Fact]
    public void Montar_SemFotoETelefone_MostraInicialESemBotao()
    {
        var html = LeadBoasVindasEmail.Montar("Maria", "Ana Consultora", null, null, null);

        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("Fazer cotação", html);
        Assert.DoesNotContain("Cancelar inscrição", html);
        Assert.Contains(">A</div>", html);
    }

    [Fact]
    public void Montar_DeveEscaparHtmlDoNomeDoCliente()
    {
        var html = LeadBoasVindasEmail.Montar("<script>x</script>", "Ana", null, null, null);

        Assert.DoesNotContain("<script>", html);
    }

    [Theory]
    [InlineData("(31) 98888-7777", true)]
    [InlineData("31988887777", true)]
    [InlineData("1234", false)]
    [InlineData(null, false)]
    public void LinkWhatsApp_SoQuandoTelefoneServe(string? telefone, bool temLink)
    {
        Assert.Equal(temLink, LeadBoasVindasEmail.LinkWhatsApp(telefone, "Ana Consultora") is not null);
    }

    [Fact]
    public async Task EnviarAsync_DeveEnviarUmaVez_ComFotoAbsoluta_EMarcarComoEnviado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (lead, _) = await CriarCenarioAsync(factory, db);
        var email = new EmailFalso();
        var servico = Criar(db, email);

        Assert.True(await servico.EnviarAsync(lead.Id, CancellationToken.None));
        Assert.False(await servico.EnviarAsync(lead.Id, CancellationToken.None)); // segunda vez não manda

        var enviado = Assert.Single(email.Enviados);
        Assert.Equal("silas@gmail.com", enviado.Para);
        Assert.Equal(LeadBoasVindasEmail.Assunto, enviado.Assunto);
        Assert.Contains("https://crm.exemplo.com.br/uploads/avatars/ana.jpg", enviado.Html);
        Assert.Contains("https://wa.me/5531988887777", enviado.Html);
        Assert.Contains("https://crm.exemplo.com.br/api/publico/descadastro?t=", enviado.Html);
        Assert.NotNull(await db.CrmLeads.AsNoTracking().Where(l => l.Id == lead.Id).Select(l => l.BoasVindasEmailEnviadoEm).FirstAsync());
    }

    [Fact]
    public async Task EnviarAsync_SeOEmailFalhar_LiberaParaTentarDeNovo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (lead, _) = await CriarCenarioAsync(factory, db);

        Assert.False(await Criar(db, new EmailFalso(resultado: false)).EnviarAsync(lead.Id, CancellationToken.None));
        Assert.Null(await db.CrmLeads.AsNoTracking().Where(l => l.Id == lead.Id).Select(l => l.BoasVindasEmailEnviadoEm).FirstAsync());

        Assert.True(await Criar(db, new EmailFalso()).EnviarAsync(lead.Id, CancellationToken.None));
    }

    [Fact]
    public async Task EnviarAsync_NaoEnvia_QuandoDesativado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (lead, _) = await CriarCenarioAsync(factory, db);
        var email = new EmailFalso();

        Assert.False(await Criar(db, email, ativo: false).EnviarAsync(lead.Id, CancellationToken.None));
        Assert.Empty(email.Enviados);
    }

    [Theory]
    [InlineData("teste@example.com", "AGV", true)]    // e-mail de teste
    [InlineData("sem-arroba", "AGV", true)]           // e-mail inválido
    [InlineData("silas@gmail.com", "APVS", true)]     // não é AGV
    [InlineData("silas@gmail.com", "AGV", false)]     // sem consultor
    public async Task EnviarAsync_NaoEnvia_QuandoNaoServe(string email, string produto, bool comConsultor)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (lead, _) = await CriarCenarioAsync(factory, db, email, produto, comConsultor);
        var falso = new EmailFalso();

        var resultado = await Criar(db, falso).EnviarAsync(lead.Id, CancellationToken.None);

        Assert.False(resultado);
        Assert.Empty(falso.Enviados);
    }

    [Fact]
    public async Task EnviarAsync_NaoEnvia_QuandoDescadastradoOuSemBaseUrl()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (lead, _) = await CriarCenarioAsync(factory, db);
        var email = new EmailFalso();

        Assert.False(await Criar(db, email, baseUrl: null).EnviarAsync(lead.Id, CancellationToken.None));

        lead.EmailDescadastradoEm = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.False(await Criar(db, email).EnviarAsync(lead.Id, CancellationToken.None));
        Assert.Empty(email.Enviados);
    }

    [Theory]
    [InlineData("a@b.com", true)]
    [InlineData("a@b", false)]
    [InlineData("a b@c.com", false)]
    [InlineData("a@example.com", false)]
    [InlineData(null, false)]
    public void EmailServe_ValidaFormatoEIgnoraDominiosDeTeste(string? email, bool esperado) =>
        Assert.Equal(esperado, LeadBoasVindasEmailService.EmailServe(email));

    [Theory]
    [InlineData("/uploads/a.jpg", "https://crm/uploads/a.jpg")]
    [InlineData("uploads/a.jpg", "https://crm/uploads/a.jpg")]
    [InlineData("https://s3.exemplo/a.jpg", "https://s3.exemplo/a.jpg")]
    [InlineData(null, null)]
    public void UrlAbsoluta_CompletaCaminhoRelativo(string? url, string? esperado) =>
        Assert.Equal(esperado, LeadBoasVindasEmailService.UrlAbsoluta(url, "https://crm"));
}
