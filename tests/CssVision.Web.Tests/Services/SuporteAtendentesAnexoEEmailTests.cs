using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Suporte: atendentes configuráveis, prints/PDF anexados, aviso por e-mail e "aguardando resposta".</summary>
public class SuporteAtendentesAnexoEEmailTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0, 0, 0];

    private static async Task<ApplicationUser> UsuarioComEmailAsync(TestDbContextFactory factory, ApplicationDbContext db, string nome, string email)
    {
        var u = await factory.CriarUsuarioAsync(db, nome);
        u.Email = email;
        u.NormalizedEmail = email.ToUpperInvariant();
        await db.SaveChangesAsync();
        return u;
    }

    private static SuporteService Servico(ApplicationDbContext db, Guid usuarioId, Mock<IPushService> push, string[]? atendentes = null, Mock<IEmailSender>? email = null) =>
        new(db, TestDbContextFactory.MockCurrentUser(usuarioId).Object, push.Object, NullLogger<SuporteService>.Instance,
            Options.Create(new SuporteOptions { Atendentes = atendentes ?? [] }), email?.Object, new FakeFileStorageService(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicBaseUrl"] = "https://crm.exemplo.com" }).Build());

    private static SuporteChamadoCreateRequest Chamado() => new("Não consigo anexar", "Problema", "A tela fica carregando.");

    [Fact]
    public async Task AtendentesConfigurados_TodosVeemEResponden_EOPadraoDeixaDeSerAtendente()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var a1 = await UsuarioComEmailAsync(factory, db, "Atendente1", "suporte1@empresa.com");
        var a2 = await UsuarioComEmailAsync(factory, db, "Atendente2", "suporte2@empresa.com");
        var padrao = await UsuarioComEmailAsync(factory, db, "Padrao", SuporteService.EmailAtendente);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var push = new Mock<IPushService>();
        string[] atendentes = ["suporte1@empresa.com", "SUPORTE2@empresa.com"];

        await Servico(db, ana.Id, push, atendentes).CriarAsync(Chamado(), CancellationToken.None);

        push.Verify(p => p.EnviarAsync(a1.Id, It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>()), Times.Once);
        push.Verify(p => p.EnviarAsync(a2.Id, It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>()), Times.Once);
        push.Verify(p => p.EnviarAsync(padrao.Id, It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.True((await Servico(db, a1.Id, push, atendentes).ListarAsync(CancellationToken.None)).Atende);
        Assert.True((await Servico(db, a2.Id, push, atendentes).ListarAsync(CancellationToken.None)).Atende);
        Assert.False((await Servico(db, padrao.Id, push, atendentes).ListarAsync(CancellationToken.None)).Atende);
    }

    [Fact]
    public async Task SemConfiguracao_OAtendentePadraoContinuaAtendendo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var padrao = await UsuarioComEmailAsync(factory, db, "Padrao", SuporteService.EmailAtendente);

        Assert.True((await Servico(db, padrao.Id, new Mock<IPushService>()).ListarAsync(CancellationToken.None)).Atende);
    }

    [Fact]
    public async Task Anexo_ImagemValida_FicaNaMensagem_ESoAnexoSemTextoVale()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var push = new Mock<IPushService>();
        var criado = await Servico(db, ana.Id, push).CriarAsync(Chamado(), CancellationToken.None);

        db.ChangeTracker.Clear();
        await using var stream = new MemoryStream(Png);
        var r = await Servico(db, ana.Id, push).ResponderComAnexoAsync(criado.Resumo.Id, null, "print da tela.PNG", Png.Length, "image/png", stream, CancellationToken.None);

        var msg = r.Mensagens.Last();
        Assert.StartsWith("/uploads/suporte/", msg.AnexoUrl);
        Assert.EndsWith(".png", msg.AnexoUrl);
        Assert.Equal("print da tela.PNG", msg.AnexoNome);
        Assert.Equal("", msg.Texto);
    }

    [Theory]
    [InlineData("virus.exe", 100)]
    [InlineData("arquivo.png", 0)]
    [InlineData("grande.png", 6 * 1024 * 1024)]
    public async Task Anexo_ExtensaoTamanhoInvalidos_Recusa(string nome, long tamanho)
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var push = new Mock<IPushService>();
        var criado = await Servico(db, ana.Id, push).CriarAsync(Chamado(), CancellationToken.None);

        db.ChangeTracker.Clear();
        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            Servico(db, ana.Id, push).ResponderComAnexoAsync(criado.Resumo.Id, "oi", nome, tamanho, "image/png", new MemoryStream(Png), CancellationToken.None));
        Assert.Equal("anexo_invalido", ex.Codigo);
    }

    [Fact]
    public async Task Anexo_ComConteudoQueNaoEPng_Recusa_PorMaisQueOSufixoSejaPng()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var push = new Mock<IPushService>();
        var criado = await Servico(db, ana.Id, push).CriarAsync(Chamado(), CancellationToken.None);

        db.ChangeTracker.Clear();
        var falso = "<html><script>alert(1)</script></html>"u8.ToArray();
        await Assert.ThrowsAsync<CrmBusinessException>(() =>
            Servico(db, ana.Id, push).ResponderComAnexoAsync(criado.Resumo.Id, null, "foto.png", falso.Length, "image/png", new MemoryStream(falso), CancellationToken.None));
    }

    [Fact]
    public async Task Anexo_EmChamadoDeOutraPessoa_ENaoEncontrado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        var push = new Mock<IPushService>();
        var criado = await Servico(db, ana.Id, push).CriarAsync(Chamado(), CancellationToken.None);

        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<CrmNotFoundException>(() =>
            Servico(db, bruna.Id, push).ResponderComAnexoAsync(criado.Resumo.Id, null, "a.png", Png.Length, "image/png", new MemoryStream(Png), CancellationToken.None));
    }

    [Fact]
    public async Task AguardandoResposta_LigaQuandoAUltimaMensagemEDoSolicitante_DesligaQuandoOSuporteResponde_EQuandoResolve()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var atendente = await UsuarioComEmailAsync(factory, db, "Atendente", SuporteService.EmailAtendente);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var push = new Mock<IPushService>();
        var criado = await Servico(db, ana.Id, push).CriarAsync(Chamado(), CancellationToken.None);
        Assert.True(criado.Resumo.AguardandoSuporte);
        Assert.NotNull(criado.Resumo.AguardandoDesde);
        Assert.True((await Servico(db, ana.Id, push).ListarAsync(CancellationToken.None)).Chamados.Single().AguardandoSuporte);

        db.ChangeTracker.Clear();
        var respondido = await Servico(db, atendente.Id, push).ResponderAsync(criado.Resumo.Id, new SuporteMensagemRequest("Olá, vou ver."), CancellationToken.None);
        Assert.False(respondido.Resumo.AguardandoSuporte);
        Assert.Null(respondido.Resumo.AguardandoDesde);
        Assert.False((await Servico(db, atendente.Id, push).ListarAsync(CancellationToken.None)).Chamados.Single().AguardandoSuporte);

        db.ChangeTracker.Clear();
        var denovo = await Servico(db, ana.Id, push).ResponderAsync(criado.Resumo.Id, new SuporteMensagemRequest("Continua igual."), CancellationToken.None);
        Assert.True(denovo.Resumo.AguardandoSuporte);

        db.ChangeTracker.Clear();
        var resolvido = await Servico(db, ana.Id, push).AlterarStatusAsync(criado.Resumo.Id, StatusChamadoSuporte.Resolvido, CancellationToken.None);
        Assert.False(resolvido.Resumo.AguardandoSuporte);
    }

    [Fact]
    public async Task EmailAvisa_AtendenteDoNovoChamado_EOSolicitanteDaResposta_ComLinkParaOChamado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var atendente = await UsuarioComEmailAsync(factory, db, "Atendente", SuporteService.EmailAtendente);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var push = new Mock<IPushService>();
        var enviados = new List<(string Para, string Assunto, string Corpo)>();
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.EnviarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string, CancellationToken>((para, _, assunto, corpo, _) => enviados.Add((para, assunto, corpo)))
            .ReturnsAsync(true);

        var criado = await Servico(db, ana.Id, push, null, email).CriarAsync(Chamado(), CancellationToken.None);
        var aoAtendente = Assert.Single(enviados);
        Assert.Equal(SuporteService.EmailAtendente, aoAtendente.Para);
        Assert.Contains("Novo chamado", aoAtendente.Assunto);
        Assert.Contains($"https://crm.exemplo.com/app/suporte?chamado={criado.Resumo.Id}", aoAtendente.Corpo);

        enviados.Clear();
        db.ChangeTracker.Clear();
        await Servico(db, atendente.Id, push, null, email).ResponderAsync(criado.Resumo.Id, new SuporteMensagemRequest("Resolvido com <b>reset</b>."), CancellationToken.None);
        var aoSolicitante = Assert.Single(enviados);
        Assert.Equal("ana@teste.local", aoSolicitante.Para);
        Assert.Contains("Suporte respondeu", aoSolicitante.Assunto);
        Assert.DoesNotContain("<b>reset</b>", aoSolicitante.Corpo); // texto da resposta nunca vira marcação no e-mail
    }

    [Fact]
    public async Task FalhaNoEmail_NaoDerrubaOChamado()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await UsuarioComEmailAsync(factory, db, "Atendente", SuporteService.EmailAtendente);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.EnviarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SMTP caiu"));

        var criado = await Servico(db, ana.Id, new Mock<IPushService>(), null, email).CriarAsync(Chamado(), CancellationToken.None);

        Assert.Equal(StatusChamadoSuporte.Aberto, criado.Resumo.Status);
        Assert.Single(await db.CrmSuporteChamados.ToListAsync());
    }
}
