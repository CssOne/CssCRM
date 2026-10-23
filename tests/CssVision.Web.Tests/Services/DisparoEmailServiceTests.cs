using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Disparo de e-mail do consultor: só clientes dele, com e-mail, na etapa escolhida; sai em nome dele com resposta para ele.</summary>
public class DisparoEmailServiceTests
{
    private sealed class EmailGravador(bool sucesso = true) : IEmailSender
    {
        public List<(string Email, string Nome, string Assunto, string Corpo, string? RemetenteNome, string? ResponderPara)> Enviados { get; } = [];

        public Task<bool> EnviarAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, CancellationToken ct) =>
            Task.FromResult(sucesso);

        public Task<bool> EnviarEmNomeDeAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, string remetenteNome, string responderParaEmail, CancellationToken ct)
        {
            Enviados.Add((destinatarioEmail, destinatarioNome, assunto, corpoHtml, remetenteNome, responderParaEmail));
            return Task.FromResult(sucesso);
        }
    }

    private sealed record Cenario(ApplicationDbContext Db, ApplicationUser Ana, ApplicationUser Bruno, CrmLeadStage Etapa, CrmLeadStage OutraEtapa, EmailGravador Email, DisparoEmailService Servico);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory, bool sucesso = true)
    {
        var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno");
        var etapa = new CrmLeadStage { Nome = "Cotação", Ordem = 10 };
        var outra = new CrmLeadStage { Nome = "Perdido", Ordem = 11 };
        db.CrmLeadStages.AddRange(etapa, outra);
        await db.SaveChangesAsync();

        var quem = new Mock<ICurrentUserService>();
        quem.SetupGet(q => q.UserId).Returns(ana.Id);
        var email = new EmailGravador(sucesso);
        var servico = new DisparoEmailService(db, quem.Object, email, new NoOpAuditSink(), NullLogger<DisparoEmailService>.Instance);
        return new Cenario(db, ana, bruno, etapa, outra, email, servico);
    }

    private static async Task LeadAsync(Cenario c, string nome, string? email, Guid responsavelId, Guid etapaId, bool arquivado = false)
    {
        c.Db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, Email = email, ResponsavelId = responsavelId, EtapaId = etapaId, Arquivado = arquivado });
        await c.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Envia_SoParaClientesDoConsultorComEmailNaEtapa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria Souza", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        await LeadAsync(c, "Sem Email", null, c.Ana.Id, c.Etapa.Id);
        await LeadAsync(c, "Outra Etapa", "outra@x.com", c.Ana.Id, c.OutraEtapa.Id);
        await LeadAsync(c, "Do Bruno", "bruno-cliente@x.com", c.Bruno.Id, c.Etapa.Id);
        await LeadAsync(c, "Arquivado", "arq@x.com", c.Ana.Id, c.Etapa.Id, arquivado: true);

        var r = await c.Servico.EnviarAsync(new DisparoEmailRequest(c.Etapa.Id, "Oferta", "Olá, {nome}!"), CancellationToken.None);

        Assert.Equal(new DisparoEmailResultadoDto(1, 1, 0), r);
        var envio = Assert.Single(c.Email.Enviados);
        Assert.Equal("maria@x.com", envio.Email);
        Assert.Equal(c.Ana.Email, envio.ResponderPara);
        Assert.Equal(c.Ana.NomeCompleto, envio.RemetenteNome);
        Assert.Contains("Olá, Maria!", envio.Corpo);
    }

    [Fact]
    public async Task ClienteComVariosCards_RecebeUmaVezSo()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        await LeadAsync(c, "Maria (2º veículo)", "MARIA@x.com", c.Ana.Id, c.Etapa.Id);

        var previa = await c.Servico.PreviaAsync(c.Etapa.Id, CancellationToken.None);

        Assert.Equal(1, previa.Destinatarios);
    }

    [Fact]
    public async Task MensagemComHtml_ViraTextoEscapado()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        await c.Servico.EnviarAsync(new DisparoEmailRequest(c.Etapa.Id, "Assunto", "<script>x</script>\nlinha 2"), CancellationToken.None);

        var corpo = Assert.Single(c.Email.Enviados).Corpo;
        Assert.DoesNotContain("<script>", corpo);
        Assert.Contains("&lt;script&gt;", corpo);
        Assert.Contains("<br>linha 2", corpo);
    }

    [Fact]
    public async Task SemDestinatarios_Recusa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarAsync(new DisparoEmailRequest(c.Etapa.Id, "Assunto", "Mensagem"), CancellationToken.None));

        Assert.Equal("sem_destinatarios", ex.Codigo);
        Assert.Empty(c.Email.Enviados);
    }

    [Fact]
    public async Task AcimaDoLimite_RecusaSemEnviarNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        for (var i = 0; i <= DisparoEmailService.LimiteDestinatarios; i++)
        {
            c.Db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = $"Cliente {i}", TipoPessoa = TipoPessoa.Fisica, Email = $"c{i}@x.com", ResponsavelId = c.Ana.Id, EtapaId = c.Etapa.Id });
        }
        await c.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarAsync(new DisparoEmailRequest(c.Etapa.Id, "Assunto", "Mensagem"), CancellationToken.None));

        Assert.Equal("limite_excedido", ex.Codigo);
        Assert.Empty(c.Email.Enviados);
    }

    [Fact]
    public async Task SmtpForaDoAr_AvisaEmVezDeFingirSucesso()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, sucesso: false);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarAsync(new DisparoEmailRequest(c.Etapa.Id, "Assunto", "Mensagem"), CancellationToken.None));

        Assert.Equal("envio_indisponivel", ex.Codigo);
    }

    [Fact]
    public async Task EtapaInexistente_Recusa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        await Assert.ThrowsAsync<CrmNotFoundException>(() => c.Servico.EnviarAsync(new DisparoEmailRequest(Guid.NewGuid(), "Assunto", "Mensagem"), CancellationToken.None));
    }
}
