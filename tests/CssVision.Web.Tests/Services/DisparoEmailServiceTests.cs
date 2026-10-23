using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Disparo de e-mail do consultor: sai da caixa dele, só para clientes dele, com e-mail, na etapa escolhida, assinado com nome e telefone.</summary>
public class DisparoEmailServiceTests
{
    private sealed class EmailGravador : IEmailSender
    {
        public bool ContaRecusada { get; set; }
        public bool ProvedorRejeitaTudo { get; set; }
        public ContaSmtp? ContaUsada { get; private set; }
        public string? RemetenteNome { get; private set; }
        public List<MensagemEmail> Enviados { get; } = [];

        public Task<bool> EnviarAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, CancellationToken ct) => Task.FromResult(true);

        public Task ValidarContaAsync(ContaSmtp conta, CancellationToken ct)
        {
            ContaUsada = conta;
            return ContaRecusada ? Task.FromException(new EmailContaException("O provedor recusou o login.")) : Task.CompletedTask;
        }

        public Task<int> EnviarLoteComContaAsync(ContaSmtp conta, string remetenteNome, IReadOnlyList<MensagemEmail> mensagens, CancellationToken ct)
        {
            if (ContaRecusada) return Task.FromException<int>(new EmailContaException("O provedor recusou o login."));
            ContaUsada = conta;
            RemetenteNome = remetenteNome;
            if (ProvedorRejeitaTudo) return Task.FromResult(0);
            Enviados.AddRange(mensagens);
            return Task.FromResult(mensagens.Count);
        }
    }

    private sealed record Cenario(ApplicationDbContext Db, ApplicationUser Ana, ApplicationUser Bruno, CrmLeadStage Etapa, CrmLeadStage OutraEtapa, EmailGravador Email, DisparoEmailService Servico);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory, bool contaConectada = true, string? telefone = "(31) 99999-8888")
    {
        var db = factory.CreateContext();
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        ana.PhoneNumber = telefone;
        var bruno = await factory.CriarUsuarioAsync(db, "Bruno");
        var etapa = new CrmLeadStage { Nome = "Cotação", Ordem = 10 };
        var outra = new CrmLeadStage { Nome = "Perdido", Ordem = 11 };
        db.CrmLeadStages.AddRange(etapa, outra);
        await db.SaveChangesAsync();

        var quem = new Mock<ICurrentUserService>();
        quem.SetupGet(q => q.UserId).Returns(ana.Id);
        var email = new EmailGravador();
        var servico = new DisparoEmailService(db, quem.Object, email, new EphemeralDataProtectionProvider(), new NoOpAuditSink(), NullLogger<DisparoEmailService>.Instance);
        if (contaConectada) await servico.SalvarContaAsync(new ContaEmailRequest("ana@gmail.com", "senha-de-app"), CancellationToken.None);
        return new Cenario(db, ana, bruno, etapa, outra, email, servico);
    }

    private static async Task LeadAsync(Cenario c, string nome, string? email, Guid responsavelId, Guid etapaId, bool arquivado = false)
    {
        c.Db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, Email = email, ResponsavelId = responsavelId, EtapaId = etapaId, Arquivado = arquivado });
        await c.Db.SaveChangesAsync();
    }

    private static DisparoEmailRequest Pedido(Cenario c, string mensagem = "Mensagem") => new(c.Etapa.Id, "Assunto", mensagem);

    [Fact]
    public async Task Envia_SoParaClientesDoConsultorComEmailNaEtapa_PelaContaDele()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria Souza", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        await LeadAsync(c, "Sem Email", null, c.Ana.Id, c.Etapa.Id);
        await LeadAsync(c, "Outra Etapa", "outra@x.com", c.Ana.Id, c.OutraEtapa.Id);
        await LeadAsync(c, "Do Bruno", "bruno-cliente@x.com", c.Bruno.Id, c.Etapa.Id);
        await LeadAsync(c, "Arquivado", "arq@x.com", c.Ana.Id, c.Etapa.Id, arquivado: true);

        var r = await c.Servico.EnviarAsync(Pedido(c, "Olá, {nome}!"), CancellationToken.None);

        Assert.Equal(new DisparoEmailResultadoDto(1, 1, 0), r);
        var envio = Assert.Single(c.Email.Enviados);
        Assert.Equal("maria@x.com", envio.DestinatarioEmail);
        Assert.Contains("Olá, Maria!", envio.CorpoHtml);
        Assert.Equal("ana@gmail.com", c.Email.ContaUsada!.Email);
        Assert.Equal("senha-de-app", c.Email.ContaUsada.Senha);
        Assert.Equal("smtp.gmail.com", c.Email.ContaUsada.Host);
        Assert.Equal(c.Ana.NomeCompleto, c.Email.RemetenteNome);
    }

    [Fact]
    public async Task NomeDoClienteTroca_TambemNoAssunto()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria Souza", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        await c.Servico.EnviarAsync(new DisparoEmailRequest(c.Etapa.Id, "{nome}, sua cotação está te esperando", "Mensagem"), CancellationToken.None);

        Assert.Equal("Maria, sua cotação está te esperando", Assert.Single(c.Email.Enviados).Assunto);
    }

    [Fact]
    public async Task Assinatura_TemNomeTelefoneEBotaoDeWhatsApp()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, telefone: "(31) 99999-8888");
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);

        var corpo = Assert.Single(c.Email.Enviados).CorpoHtml;
        Assert.Contains(c.Ana.NomeCompleto, corpo);
        Assert.Contains("(31) 99999-8888", corpo);
        Assert.Contains("https://wa.me/5531999998888", corpo);
        Assert.Contains("Chamar no WhatsApp", corpo);
    }

    [Fact]
    public async Task SemTelefoneCadastrado_AssinaSoComNomeEEmail_SemBotao()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, telefone: null);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);

        var corpo = Assert.Single(c.Email.Enviados).CorpoHtml;
        Assert.DoesNotContain("wa.me", corpo);
        Assert.DoesNotContain("Telefone/WhatsApp", corpo);
        Assert.Contains(c.Ana.NomeCompleto, corpo);
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

        await c.Servico.EnviarAsync(Pedido(c, "<script>x</script>\nlinha 2"), CancellationToken.None);

        var corpo = Assert.Single(c.Email.Enviados).CorpoHtml;
        Assert.DoesNotContain("<script>", corpo);
        Assert.Contains("&lt;script&gt;", corpo);
        Assert.Contains("<br>linha 2", corpo);
    }

    [Fact]
    public async Task SemContaConectada_PedeParaConectarOEmail()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, contaConectada: false);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarAsync(Pedido(c), CancellationToken.None));

        Assert.Equal("conta_nao_configurada", ex.Codigo);
        Assert.Empty(c.Email.Enviados);
    }

    [Fact]
    public async Task SenhaGuardadaProtegida_ENuncaVoltaParaATela()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var guardada = await c.Db.CrmContasEmail.AsNoTracking().SingleAsync();
        var dto = await c.Servico.ObterContaAsync(CancellationToken.None);

        Assert.DoesNotContain("senha-de-app", guardada.SenhaProtegida);
        Assert.Equal("ana@gmail.com", dto!.Email);
    }

    [Fact]
    public async Task SalvarConta_TrocaAContaExistenteEmVezDeDuplicar()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        await c.Servico.SalvarContaAsync(new ContaEmailRequest("ana@empresa.com.br", "outra-senha", "smtp.empresa.com.br", 465), CancellationToken.None);

        var conta = await c.Db.CrmContasEmail.AsNoTracking().SingleAsync();
        Assert.Equal("ana@empresa.com.br", conta.Email);
        Assert.Equal("smtp.empresa.com.br", conta.Host);
        Assert.Equal(465, conta.Port);
    }

    [Fact]
    public async Task SalvarConta_ProvedorDesconhecidoSemServidor_Recusa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, contaConectada: false);

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.SalvarContaAsync(new ContaEmailRequest("ana@empresa.com.br", "senha"), CancellationToken.None));

        Assert.Equal("servidor_obrigatorio", ex.Codigo);
    }

    [Fact]
    public async Task SalvarConta_LoginRecusado_NaoGuardaNada()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, contaConectada: false);
        c.Email.ContaRecusada = true;

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.SalvarContaAsync(new ContaEmailRequest("ana@gmail.com", "errada"), CancellationToken.None));

        Assert.Equal("conta_invalida", ex.Codigo);
        Assert.Empty(await c.Db.CrmContasEmail.ToListAsync());
    }

    [Fact]
    public async Task RemoverConta_ApagaAConta()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        await c.Servico.RemoverContaAsync(CancellationToken.None);

        Assert.Null(await c.Servico.ObterContaAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SemDestinatarios_Recusa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarAsync(Pedido(c), CancellationToken.None));

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

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarAsync(Pedido(c), CancellationToken.None));

        Assert.Equal("limite_excedido", ex.Codigo);
        Assert.Empty(c.Email.Enviados);
    }

    [Fact]
    public async Task LoginRecusadoNoEnvio_AvisaParaReconectar()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        c.Email.ContaRecusada = true;

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarAsync(Pedido(c), CancellationToken.None));

        Assert.Equal("conta_invalida", ex.Codigo);
    }

    [Fact]
    public async Task ProvedorRejeitaTudo_AvisaEmVezDeFingirSucesso()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        c.Email.ProvedorRejeitaTudo = true;

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarAsync(Pedido(c), CancellationToken.None));

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
