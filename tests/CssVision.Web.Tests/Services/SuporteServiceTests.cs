using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Aba Suporte: cada usuário vê só os próprios chamados; o atendente (naome1248@gmail.com) vê todos e é avisado.</summary>
public class SuporteServiceTests
{
    private static async Task<ApplicationUser> Atendente(TestDbContextFactory factory, ApplicationDbContext db)
    {
        var usuario = await factory.CriarUsuarioAsync(db, "Naome");
        usuario.Email = SuporteService.EmailAtendente;
        usuario.NormalizedEmail = SuporteService.EmailAtendente.ToUpperInvariant();
        await db.SaveChangesAsync();
        return usuario;
    }

    private static SuporteService Servico(ApplicationDbContext db, Guid usuarioId, Mock<IPushService> push) =>
        new(db, TestDbContextFactory.MockCurrentUser(usuarioId).Object, push.Object, NullLogger<SuporteService>.Instance);

    private static SuporteChamadoCreateRequest Chamado(string assunto = "Não consigo entrar") => new(assunto, "Problema", "Aparece erro ao fazer login.");

    [Fact]
    public async Task NovoChamado_ChegaAoAtendente_ComPush_ESoOSolicitanteEOAtendenteOVeem()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var atendente = await Atendente(factory, db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruna = await factory.CriarUsuarioAsync(db, "Bruna");
        var push = new Mock<IPushService>();

        var criado = await Servico(db, ana.Id, push).CriarAsync(Chamado(), CancellationToken.None);

        push.Verify(p => p.EnviarAsync(atendente.Id, It.Is<PushMensagem>(m => m.Titulo.Contains("Novo chamado")), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(StatusChamadoSuporte.Aberto, criado.Resumo.Status);

        var doAtendente = await Servico(db, atendente.Id, push).ListarAsync(CancellationToken.None);
        Assert.True(doAtendente.Atende);
        Assert.Single(doAtendente.Chamados);
        Assert.Equal("ana@teste.local", doAtendente.Chamados[0].SolicitanteEmail);

        var daAna = await Servico(db, ana.Id, push).ListarAsync(CancellationToken.None);
        Assert.False(daAna.Atende);
        Assert.Single(daAna.Chamados);
        Assert.Null(daAna.Chamados[0].SolicitanteEmail);

        var daBruna = await Servico(db, bruna.Id, push).ListarAsync(CancellationToken.None);
        Assert.Empty(daBruna.Chamados);
        await Assert.ThrowsAsync<CrmNotFoundException>(() => Servico(db, bruna.Id, push).ObterAsync(criado.Resumo.Id, CancellationToken.None));
        await Assert.ThrowsAsync<CrmNotFoundException>(() =>
            Servico(db, bruna.Id, push).ResponderAsync(criado.Resumo.Id, new SuporteMensagemRequest("oi"), CancellationToken.None));
    }

    [Fact]
    public async Task Resposta_DoAtendente_AvisaOSolicitante_EColocaEmAtendimento_EResolverNotifica()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var atendente = await Atendente(factory, db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var push = new Mock<IPushService>();
        var criado = await Servico(db, ana.Id, push).CriarAsync(Chamado(), CancellationToken.None);

        db.ChangeTracker.Clear(); // SQLite não tem xmin: cada operação começa de um contexto limpo.
        var respondido = await Servico(db, atendente.Id, push).ResponderAsync(criado.Resumo.Id, new SuporteMensagemRequest("Já resetei sua senha."), CancellationToken.None);
        Assert.Equal(StatusChamadoSuporte.EmAtendimento, respondido.Resumo.Status);
        Assert.True(respondido.Mensagens.Last().DoSuporte);
        Assert.Equal("Suporte", respondido.Mensagens.Last().AutorNome);
        push.Verify(p => p.EnviarAsync(ana.Id, It.Is<PushMensagem>(m => m.Titulo == "Suporte respondeu"), It.IsAny<CancellationToken>()), Times.Once);

        db.ChangeTracker.Clear(); // SQLite não tem xmin: cada operação começa de um contexto limpo.
        var resolvido = await Servico(db, atendente.Id, push).AlterarStatusAsync(criado.Resumo.Id, StatusChamadoSuporte.Resolvido, CancellationToken.None);
        Assert.Equal(StatusChamadoSuporte.Resolvido, resolvido.Resumo.Status);
        push.Verify(p => p.EnviarAsync(ana.Id, It.Is<PushMensagem>(m => m.Titulo == "Chamado resolvido"), It.IsAny<CancellationToken>()), Times.Once);

        db.ChangeTracker.Clear(); // SQLite não tem xmin: cada operação começa de um contexto limpo.
        // O solicitante responder reabre.
        var reaberto = await Servico(db, ana.Id, push).ResponderAsync(criado.Resumo.Id, new SuporteMensagemRequest("Voltou a dar erro."), CancellationToken.None);
        Assert.Equal(StatusChamadoSuporte.Aberto, reaberto.Resumo.Status);
        Assert.Equal(3, reaberto.Mensagens.Count);
    }

    [Fact]
    public async Task Solicitante_NaoColocaChamadoEmAtendimento()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await Atendente(factory, db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var push = new Mock<IPushService>();
        var criado = await Servico(db, ana.Id, push).CriarAsync(Chamado(), CancellationToken.None);

        db.ChangeTracker.Clear();
        await Assert.ThrowsAsync<CrmForbiddenException>(() =>
            Servico(db, ana.Id, push).AlterarStatusAsync(criado.Resumo.Id, StatusChamadoSuporte.EmAtendimento, CancellationToken.None));
        db.ChangeTracker.Clear(); // SQLite não tem xmin: cada operação começa de um contexto limpo.
        var encerrado = await Servico(db, ana.Id, push).AlterarStatusAsync(criado.Resumo.Id, StatusChamadoSuporte.Resolvido, CancellationToken.None);
        Assert.Equal(StatusChamadoSuporte.Resolvido, encerrado.Resumo.Status);
    }
}
