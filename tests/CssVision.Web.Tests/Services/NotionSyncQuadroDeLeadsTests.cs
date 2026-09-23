using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Regras do quadro de leads aplicadas ao Status vindo do Notion (NotionEtapaLead +
/// NotionSyncService.AplicarStatusDoNotionAsync).
/// </summary>
public class NotionSyncQuadroDeLeadsTests
{
    /// <summary>Colunas ativas do quadro hoje (depois das migrações de 18/09).</summary>
    private static readonly string[] ColunasAtivas =
    [
        "Em atendimento (Leads)", "Em atendimento (Indicação)", "Cotação",
        "Venda concluída (Leads)", "Venda concluída (Indicação)", "Perdido", "Não responde", "Não fazemos"
    ];

    private static async Task<(ApplicationDbContext Db, NotionSyncService Service, Dictionary<string, Guid> Etapas)> PrepararAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var etapas = new Dictionary<string, Guid>();
        for (var i = 0; i < ColunasAtivas.Length; i++)
        {
            etapas[ColunasAtivas[i]] = (await factory.ObterOuCriarEtapaLeadAsync(db, ColunasAtivas[i], i + 1)).Id;
        }

        // UserManager não é usado por AplicarStatusDoNotionAsync.
        var service = new NotionSyncService(db, null!, NullLogger<NotionSyncService>.Instance);
        return (db, service, etapas);
    }

    private static CrmLead NovoLead(Guid? etapaId = null, string? tipoIndicacao = "Lead", string? notionStatus = null) => new()
    {
        NomeOuRazaoSocial = "Cliente",
        TipoPessoa = TipoPessoa.Fisica,
        TipoIndicacao = tipoIndicacao,
        EtapaId = etapaId,
        NotionStatus = notionStatus,
    };

    [Theory]
    [InlineData("EM ATENDIMENTO", "Lead", "Em atendimento (Leads)")]
    [InlineData("EM ATENDIMENTO", "Indicação", "Em atendimento (Indicação)")]
    [InlineData("COTAÇÃO", "Lead", "Cotação")]
    [InlineData("PRÉ CADASTRO", "Lead", "Cotação")]
    [InlineData("VENDA CONCLUIDA", "Lead", "Venda concluída (Leads)")]
    [InlineData("VENDA CONCLUIDA", "Indicação", "Venda concluída (Indicação)")]
    [InlineData("PERDIDO", "Lead", "Perdido")]
    [InlineData("RECUSA/INATIVA", "Lead", "Perdido")]
    [InlineData("NÃO FAZEMOS ", "Lead", "Não fazemos")]
    public async Task LeadSemEtapa_VaiParaAColunaDoStatusDoNotion(string status, string tipoIndicacao, string colunaEsperada)
    {
        using var factory = new TestDbContextFactory();
        var (db, service, etapas) = await PrepararAsync(factory);
        var lead = NovoLead(tipoIndicacao: tipoIndicacao);

        var mudou = await service.AplicarStatusDoNotionAsync(lead, status, null, null, etapas, CancellationToken.None);

        Assert.True(mudou);
        Assert.Equal(etapas[colunaEsperada], lead.EtapaId);
        Assert.Equal(status.Trim(), lead.NotionStatus);
    }

    [Fact]
    public async Task CardSemStatusNoNotion_FicaEmSemEtapa()
    {
        using var factory = new TestDbContextFactory();
        var (_, service, etapas) = await PrepararAsync(factory);
        var lead = NovoLead();

        var mudou = await service.AplicarStatusDoNotionAsync(lead, null, null, null, etapas, CancellationToken.None);

        Assert.False(mudou);
        Assert.Null(lead.EtapaId);
    }

    [Fact]
    public async Task LeadJaEmOutraColuna_AcompanhaAMudancaDeStatusNoNotion()
    {
        using var factory = new TestDbContextFactory();
        var (_, service, etapas) = await PrepararAsync(factory);
        var lead = NovoLead(etapas["Em atendimento (Leads)"], notionStatus: "EM ATENDIMENTO");

        var mudou = await service.AplicarStatusDoNotionAsync(lead, "COTAÇÃO", null, null, etapas, CancellationToken.None);

        Assert.True(mudou);
        Assert.Equal(etapas["Cotação"], lead.EtapaId);
    }

    [Fact]
    public async Task LeadAntigoSemStatusRegistrado_EAlinhadoAoNotionNaPrimeiraLeitura()
    {
        using var factory = new TestDbContextFactory();
        var (_, service, etapas) = await PrepararAsync(factory);
        // Lead criado pela sincronização antiga: coluna definida só na criação, NotionStatus ainda vazio.
        var lead = NovoLead(etapas["Em atendimento (Leads)"], notionStatus: null);

        await service.AplicarStatusDoNotionAsync(lead, "PERDIDO", "Não responde", null, etapas, CancellationToken.None);

        Assert.Equal(etapas["Perdido"], lead.EtapaId);
    }

    [Fact]
    public async Task MovimentoFeitoNoCrm_NaoEDesfeito_EnquantoOStatusNoNotionNaoMuda()
    {
        using var factory = new TestDbContextFactory();
        var (_, service, etapas) = await PrepararAsync(factory);
        // Vendedora moveu para "Não responde" no CRM; no Notion o card continua EM ATENDIMENTO.
        var lead = NovoLead(etapas["Não responde"], notionStatus: "EM ATENDIMENTO");

        var mudou = await service.AplicarStatusDoNotionAsync(lead, "EM ATENDIMENTO", null, null, etapas, CancellationToken.None);

        Assert.False(mudou);
        Assert.Equal(etapas["Não responde"], lead.EtapaId);
    }

    [Fact]
    public async Task Perdido_SempreRecebeMotivoDePerda_UsandoOMotivoDoNotion()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, etapas) = await PrepararAsync(factory);
        var lead = NovoLead(etapas["Cotação"], notionStatus: "COTAÇÃO");
        lead.VeiculoNaoAtendido = "resto de outra coluna";
        db.CrmLeads.Add(lead);

        await service.AplicarStatusDoNotionAsync(lead, "PERDIDO", "Financeiro", null, etapas, CancellationToken.None);
        await db.SaveChangesAsync();

        var motivo = await db.CrmLossReasons.SingleAsync(m => m.Id == lead.MotivoPerdaId);
        Assert.Equal("Financeiro", motivo.Descricao);
        Assert.Null(lead.VeiculoNaoAtendido);
    }

    [Theory]
    [InlineData("PERDIDO", "Não informado no Notion")]
    [InlineData("RECUSA/INATIVA", "Recusa/Inativa")]
    public async Task Perdido_SemMotivoNoNotion_RecebeMotivoPadrao(string status, string motivoEsperado)
    {
        using var factory = new TestDbContextFactory();
        var (db, service, etapas) = await PrepararAsync(factory);
        var lead = NovoLead();
        db.CrmLeads.Add(lead);

        await service.AplicarStatusDoNotionAsync(lead, status, null, null, etapas, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(motivoEsperado, (await db.CrmLossReasons.SingleAsync(m => m.Id == lead.MotivoPerdaId)).Descricao);
    }

    [Fact]
    public async Task MotivoDePerdaExistente_EReaproveitado_SemDuplicar()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, etapas) = await PrepararAsync(factory);
        db.CrmLossReasons.Add(new CrmLossReason { Descricao = "Não responde" });
        await db.SaveChangesAsync();

        var a = NovoLead();
        var b = NovoLead();
        db.CrmLeads.AddRange(a, b);
        await service.AplicarStatusDoNotionAsync(a, "PERDIDO", "não responde", null, etapas, CancellationToken.None);
        await service.AplicarStatusDoNotionAsync(b, "PERDIDO", "Não responde", null, etapas, CancellationToken.None);
        await db.SaveChangesAsync();

        Assert.Equal(1, await db.CrmLossReasons.CountAsync(m => m.Descricao == "Não responde"));
        Assert.Equal(a.MotivoPerdaId, b.MotivoPerdaId);
    }

    [Fact]
    public async Task NaoFazemos_SempreRecebeVeiculoNaoAtendido_ELimpaOMotivoDePerda()
    {
        using var factory = new TestDbContextFactory();
        var (db, service, etapas) = await PrepararAsync(factory);
        var lead = NovoLead(etapas["Perdido"], notionStatus: "PERDIDO");
        var motivo = new CrmLossReason { Descricao = "Preço" };
        db.CrmLossReasons.Add(motivo);
        lead.MotivoPerdaId = motivo.Id;

        await service.AplicarStatusDoNotionAsync(lead, "NÃO FAZEMOS ", null, "Caminhão 3/4", etapas, CancellationToken.None);

        Assert.Equal(etapas["Não fazemos"], lead.EtapaId);
        Assert.Equal("Caminhão 3/4", lead.VeiculoNaoAtendido);
        Assert.Null(lead.MotivoPerdaId);
    }

    [Fact]
    public async Task StatusSemColunaCorrespondente_MantemOLeadOndeEsta()
    {
        using var factory = new TestDbContextFactory();
        var (_, service, etapas) = await PrepararAsync(factory);
        var lead = NovoLead(etapas["Cotação"], notionStatus: "COTAÇÃO");

        var mudou = await service.AplicarStatusDoNotionAsync(lead, "STATUS NOVO DO NOTION", null, null, etapas, CancellationToken.None);

        Assert.False(mudou);
        Assert.Equal(etapas["Cotação"], lead.EtapaId);
    }

    [Fact]
    public void Resolver_ColunaUnicaAntiga_QuandoAindaNaoDivididaEmLeadsIndicacao()
    {
        var etapas = new Dictionary<string, Guid> { ["Venda concluída"] = Guid.NewGuid() };

        var (reconhecido, etapaId, _) = NotionEtapaLead.Resolver("VENDA CONCLUIDA", ehIndicacao: true, etapas);

        Assert.True(reconhecido);
        Assert.Equal(etapas["Venda concluída"], etapaId);
    }
}
