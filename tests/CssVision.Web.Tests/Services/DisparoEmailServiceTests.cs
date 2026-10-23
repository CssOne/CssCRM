using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Services.Security;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Disparo de e-mail do consultor: entra na fila e o processador envia em lotes pela caixa dele, só para clientes dele, com e-mail, na etapa escolhida,
/// assinado com nome e telefone e com link de descadastro; o histórico mostra o andamento.
/// </summary>
public class DisparoEmailServiceTests
{
    private sealed class EmailGravador : IEmailSender
    {
        public bool ContaRecusada { get; set; }
        public bool ProvedorRejeitaTudo { get; set; }
        public HashSet<string> RejeitaOsEmails { get; } = [];
        public ContaSmtp? ContaUsada { get; private set; }
        public string? RemetenteNome { get; private set; }
        public int Lotes { get; private set; }
        public List<MensagemEmail> Enviados { get; } = [];

        public Task<bool> EnviarAsync(string destinatarioEmail, string destinatarioNome, string assunto, string corpoHtml, CancellationToken ct) => Task.FromResult(true);

        public Task ValidarContaAsync(ContaSmtp conta, CancellationToken ct)
        {
            ContaUsada = conta;
            return ContaRecusada ? Task.FromException(new EmailContaException("O provedor recusou o login.")) : Task.CompletedTask;
        }

        public Task<IReadOnlyList<bool>> EnviarLoteComContaAsync(ContaSmtp conta, string remetenteNome, IReadOnlyList<MensagemEmail> mensagens, CancellationToken ct)
        {
            if (ContaRecusada) return Task.FromException<IReadOnlyList<bool>>(new EmailContaException("O provedor recusou o login."));
            Lotes++;
            ContaUsada = conta;
            RemetenteNome = remetenteNome;
            var resultado = mensagens.Select(m => !ProvedorRejeitaTudo && !RejeitaOsEmails.Contains(m.DestinatarioEmail)).ToList();
            Enviados.AddRange(mensagens.Where((_, i) => resultado[i]));
            return Task.FromResult<IReadOnlyList<bool>>(resultado);
        }
    }

    private sealed class AmbienteFalso(string nome) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nome;
        public string ApplicationName { get; set; } = "teste";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed record Cenario(
        ApplicationDbContext Db, ApplicationUser Ana, ApplicationUser Bruno, CrmLeadStage Etapa, CrmLeadStage OutraEtapa, EmailGravador Email,
        DisparoEmailService Servico, DisparoEmailProcessador Processador, IDescadastroEmailTokens Tokens);

    private static async Task<Cenario> MontarAsync(
        TestDbContextFactory factory, bool contaConectada = true, string? telefone = "(31) 99999-8888", string? baseUrl = "https://crm.exemplo.com", string ambiente = "Production")
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
        quem.SetupGet(q => q.IsAuthenticated).Returns(true);
        var email = new EmailGravador();
        var dp = new EphemeralDataProtectionProvider();
        var segredos = new SegredoProtetor(new ConfigurationBuilder().Build(), dp, NullLogger<SegredoProtetor>.Instance);
        var tokens = new DescadastroEmailTokens(dp);
        var configuracao = new ConfigurationBuilder().AddInMemoryCollection(baseUrl is null ? [] : new Dictionary<string, string?> { ["App:PublicBaseUrl"] = baseUrl }).Build();
        var servico = new DisparoEmailService(db, quem.Object, email, segredos, new NoOpAuditSink());
        var processador = new DisparoEmailProcessador(db, email, segredos, tokens, configuracao, new AmbienteFalso(ambiente), NullLogger<DisparoEmailProcessador>.Instance);
        if (contaConectada) await servico.SalvarContaAsync(new ContaEmailRequest("ana@gmail.com", "senha-de-app"), CancellationToken.None);
        return new Cenario(db, ana, bruno, etapa, outra, email, servico, processador, tokens);
    }

    private static async Task<CrmLead> LeadAsync(Cenario c, string nome, string? email, Guid responsavelId, Guid etapaId, bool arquivado = false)
    {
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, Email = email, ResponsavelId = responsavelId, EtapaId = etapaId, Arquivado = arquivado,
        };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        return lead;
    }

    private static DisparoEmailRequest Pedido(Cenario c, string mensagem = "Mensagem", string assunto = "Assunto") => new(c.Etapa.Id, assunto, mensagem);

    /// <summary>Roda a fila até esvaziar (o serviço em segundo plano faz isso aos poucos; no teste, de uma vez).</summary>
    private static async Task EsvaziarFilaAsync(Cenario c)
    {
        for (var i = 0; i < 50 && await c.Processador.ProcessarProximoAsync(CancellationToken.None); i++) c.Db.ChangeTracker.Clear();
        c.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task EnviarEntraNaFila_ENaoMandaNada_AteOProcessadorRodar()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria Souza", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        var disparo = await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);

        Assert.Equal(StatusDisparoEmail.Pendente, disparo.Status);
        Assert.Equal(1, disparo.Total);
        Assert.Empty(c.Email.Enviados);

        await EsvaziarFilaAsync(c);
        var historico = Assert.Single(await c.Servico.HistoricoAsync(CancellationToken.None));
        Assert.Equal(StatusDisparoEmail.Concluido, historico.Status);
        Assert.Equal(1, historico.Enviados);
        Assert.Equal(0, historico.Falhas);
        Assert.NotNull(historico.ConcluidoEm);
    }

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

        await c.Servico.EnviarAsync(Pedido(c, "Olá, {nome}!"), CancellationToken.None);
        await EsvaziarFilaAsync(c);

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

        await c.Servico.EnviarAsync(Pedido(c, assunto: "{nome}, sua cotação está te esperando"), CancellationToken.None);
        await EsvaziarFilaAsync(c);

        Assert.Equal("Maria, sua cotação está te esperando", Assert.Single(c.Email.Enviados).Assunto);
    }

    [Fact]
    public async Task Assinatura_TemNomeTelefoneBotaoDeWhatsAppELinkDeDescadastro()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, telefone: "(31) 99999-8888");
        var lead = await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);
        await EsvaziarFilaAsync(c);

        var envio = Assert.Single(c.Email.Enviados);
        Assert.Contains(c.Ana.NomeCompleto, envio.CorpoHtml);
        Assert.Contains("(31) 99999-8888", envio.CorpoHtml);
        Assert.Contains("https://wa.me/5531999998888", envio.CorpoHtml);
        Assert.Contains("Chamar no WhatsApp", envio.CorpoHtml);
        Assert.Contains("Cancelar inscrição", envio.CorpoHtml);
        Assert.StartsWith("https://crm.exemplo.com/api/publico/descadastro?t=", envio.UrlDescadastro);
        var token = Uri.UnescapeDataString(envio.UrlDescadastro![envio.UrlDescadastro.IndexOf("t=", StringComparison.Ordinal)..][2..]);
        Assert.Equal(lead.Id, c.Tokens.Ler(token));
    }

    [Fact]
    public async Task SemTelefoneCadastrado_AssinaSoComNomeEEmail_SemBotao()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, telefone: null);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);
        await EsvaziarFilaAsync(c);

        var corpo = Assert.Single(c.Email.Enviados).CorpoHtml;
        Assert.DoesNotContain("wa.me", corpo);
        Assert.DoesNotContain("Telefone/WhatsApp", corpo);
        Assert.Contains(c.Ana.NomeCompleto, corpo);
    }

    [Fact]
    public async Task QuemCancelouAInscricao_NaoRecebe_ENemOutrosCardsComOMesmoEmail()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var maria = await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        await LeadAsync(c, "Maria (2º veículo)", "MARIA@x.com", c.Ana.Id, c.OutraEtapa.Id);
        await LeadAsync(c, "João", "joao@x.com", c.Ana.Id, c.Etapa.Id);

        Assert.Equal(2, await DescadastroEmail.DescadastrarAsync(c.Db, maria.Id, CancellationToken.None));

        var previa = await c.Servico.PreviaAsync(c.Etapa.Id, CancellationToken.None);
        Assert.Equal(1, previa.Destinatarios);
        Assert.Equal(1, previa.Descadastrados);

        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);
        await EsvaziarFilaAsync(c);
        Assert.Equal("joao@x.com", Assert.Single(c.Email.Enviados).DestinatarioEmail);
    }

    [Fact]
    public async Task CancelouDepoisDeEntrarNaFila_EPuladoNoEnvio()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var maria = await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        await LeadAsync(c, "João", "joao@x.com", c.Ana.Id, c.Etapa.Id);
        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);

        await DescadastroEmail.DescadastrarAsync(c.Db, maria.Id, CancellationToken.None);
        await EsvaziarFilaAsync(c);

        Assert.Equal("joao@x.com", Assert.Single(c.Email.Enviados).DestinatarioEmail);
        var historico = Assert.Single(await c.Servico.HistoricoAsync(CancellationToken.None));
        Assert.Equal(1, historico.Enviados);
        Assert.Equal(1, historico.Falhas);
    }

    [Fact]
    public async Task TokenAdulterado_NaoDescadastraNinguem()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        Assert.Null(c.Tokens.Ler("lixo"));
        Assert.Null(c.Tokens.Ler(null));
        Assert.Null(c.Tokens.Ler(c.Tokens.Gerar(lead.Id) + "x"));
        Assert.Equal(lead.Id, c.Tokens.Ler(c.Tokens.Gerar(lead.Id)));
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
        await EsvaziarFilaAsync(c);

        var corpo = Assert.Single(c.Email.Enviados).CorpoHtml;
        Assert.DoesNotContain("<script>", corpo);
        Assert.Contains("&lt;script&gt;", corpo);
        Assert.Contains("<br>linha 2", corpo);
    }

    [Fact]
    public async Task EmailEnviado_VirandoNotaNaLinhaDoTempo_EAtualizaOUltimoContato()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);

        await c.Servico.EnviarAsync(Pedido(c, assunto: "Oferta"), CancellationToken.None);
        await EsvaziarFilaAsync(c);

        var nota = await c.Db.CrmNotes.AsNoTracking().SingleAsync(n => n.LeadId == lead.Id);
        Assert.Contains("Oferta", nota.Texto);
        Assert.Equal(c.Ana.Id, nota.AutorId);
        Assert.NotNull((await c.Db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == lead.Id)).UltimoContatoEm);
    }

    [Fact]
    public async Task ListaGrande_VaiEmLotes_EContaOsQueFalharam()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var total = DisparoEmailProcessador.TamanhoLote * 2 + 5;
        for (var i = 0; i < total; i++) c.Db.CrmLeads.Add(new CrmLead { NomeOuRazaoSocial = $"Cliente {i:000}", TipoPessoa = TipoPessoa.Fisica, Email = $"c{i}@x.com", ResponsavelId = c.Ana.Id, EtapaId = c.Etapa.Id });
        await c.Db.SaveChangesAsync();
        c.Email.RejeitaOsEmails.Add("c3@x.com");

        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);
        await EsvaziarFilaAsync(c);

        Assert.Equal(3, c.Email.Lotes);
        var historico = Assert.Single(await c.Servico.HistoricoAsync(CancellationToken.None));
        Assert.Equal(StatusDisparoEmail.Concluido, historico.Status);
        Assert.Equal(total - 1, historico.Enviados);
        Assert.Equal(1, historico.Falhas);
    }

    [Fact]
    public async Task UmDisparoPorVez_NaFilaDoMesmoConsultor()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<CrmBusinessException>(() => c.Servico.EnviarAsync(Pedido(c), CancellationToken.None));
        Assert.Equal("disparo_em_andamento", ex.Codigo);

        await EsvaziarFilaAsync(c);
        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None); // terminou: pode de novo
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
    }

    [Fact]
    public async Task AcimaDoLimite_RecusaSemEnfileirarNada()
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
        Assert.Empty(await c.Db.CrmDisparosEmail.ToListAsync());
    }

    [Fact]
    public async Task LoginRecusadoNoEnvio_DisparoFalha_ComOMotivo_ENadaSai()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);
        c.Email.ContaRecusada = true;

        await EsvaziarFilaAsync(c);

        var historico = Assert.Single(await c.Servico.HistoricoAsync(CancellationToken.None));
        Assert.Equal(StatusDisparoEmail.Falhou, historico.Status);
        Assert.Contains("recusou o login", historico.Erro);
        Assert.Equal(1, historico.Falhas);
        Assert.Empty(c.Email.Enviados);
    }

    [Fact]
    public async Task ProvedorRejeitaTudo_DisparoFalha()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);
        c.Email.ProvedorRejeitaTudo = true;

        await EsvaziarFilaAsync(c);

        Assert.Equal(StatusDisparoEmail.Falhou, Assert.Single(await c.Servico.HistoricoAsync(CancellationToken.None)).Status);
    }

    [Fact]
    public async Task SemUrlPublicaEmProducao_DisparoFalha_PorqueNaoHaveriaLinkDeDescadastro()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory, baseUrl: null, ambiente: "Production");
        await LeadAsync(c, "Maria", "maria@x.com", c.Ana.Id, c.Etapa.Id);
        await c.Servico.EnviarAsync(Pedido(c), CancellationToken.None);

        await EsvaziarFilaAsync(c);

        var historico = Assert.Single(await c.Servico.HistoricoAsync(CancellationToken.None));
        Assert.Equal(StatusDisparoEmail.Falhou, historico.Status);
        Assert.Contains("App:PublicBaseUrl", historico.Erro);
        Assert.Empty(c.Email.Enviados);
    }

    [Fact]
    public async Task HistoricoMostraSoOsDisparosDoProprioConsultor()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        c.Db.CrmDisparosEmail.Add(new CrmDisparoEmail { UsuarioId = c.Bruno.Id, EtapaId = c.Etapa.Id, EtapaNome = "Cotação", Assunto = "Do Bruno", Mensagem = "x", Total = 1 });
        await c.Db.SaveChangesAsync();

        Assert.Empty(await c.Servico.HistoricoAsync(CancellationToken.None));
    }

    [Fact]
    public async Task EtapaInexistente_Recusa()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);

        await Assert.ThrowsAsync<CrmNotFoundException>(() => c.Servico.EnviarAsync(new DisparoEmailRequest(Guid.NewGuid(), "Assunto", "Mensagem"), CancellationToken.None));
    }
}
