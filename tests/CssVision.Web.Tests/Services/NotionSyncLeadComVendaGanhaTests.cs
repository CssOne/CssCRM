using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Lead com oportunidade ganha nunca fica fora de "Venda concluída", mesmo com o Status do Notion atrasado.</summary>
public class NotionSyncLeadComVendaGanhaTests
{
    private static readonly string[] Colunas =
        ["Em atendimento (Leads)", "Em atendimento (Indicação)", "Cotação", "Venda concluída (Leads)", "Venda concluída (Indicação)", "Perdido", "Não fazemos"];

    private sealed record Cenario(ApplicationDbContext Db, NotionSyncService Servico, Dictionary<string, Guid> Etapas, CrmPipelineStage Ganho, Guid VendedorId);

    private static async Task<Cenario> MontarAsync(TestDbContextFactory factory)
    {
        var db = factory.CreateContext();
        var etapas = new Dictionary<string, Guid>();
        for (var i = 0; i < Colunas.Length; i++) etapas[Colunas[i]] = (await factory.ObterOuCriarEtapaLeadAsync(db, Colunas[i], i + 1)).Id;
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        return new Cenario(db, new NotionSyncService(db, null!, NullLogger<NotionSyncService>.Instance), etapas, ganho, vendedor.Id);
    }

    private static async Task<CrmLead> LeadAsync(Cenario c, string coluna, string? tipo = "Lead", bool comVenda = true, bool vendaExcluida = false)
    {
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, TipoIndicacao = tipo, ResponsavelId = c.VendedorId, EtapaId = c.Etapas[coluna],
        };
        c.Db.CrmLeads.Add(lead);
        await c.Db.SaveChangesAsync();
        if (comVenda)
        {
            c.Db.CrmOpportunities.Add(new CrmOpportunity { LeadId = lead.Id, Titulo = "V", ResponsavelId = c.VendedorId, EtapaId = c.Ganho.Id, Arquivado = vendaExcluida });
            await c.Db.SaveChangesAsync();
        }
        return lead;
    }

    [Theory]
    [InlineData("Cotação", "Lead", "Venda concluída (Leads)")]
    [InlineData("Em atendimento (Leads)", "Lead", "Venda concluída (Leads)")]
    [InlineData("Em atendimento (Indicação)", "Indicação", "Venda concluída (Indicação)")]
    public async Task LeadComVendaGanhaParadoEmOutraColuna_VaiParaVendaConcluida(string coluna, string tipo, string esperada)
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, coluna, tipo);

        var mudou = await c.Servico.GarantirColunaDeVendaAsync(lead, false, c.Etapas, CancellationToken.None);

        Assert.True(mudou);
        Assert.Equal(c.Etapas[esperada], lead.EtapaId);
    }

    [Fact]
    public async Task LeadSemEtapa_ComVendaGanha_VaiParaVendaConcluida()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, "Cotação");
        lead.EtapaId = null;

        Assert.True(await c.Servico.GarantirColunaDeVendaAsync(lead, false, c.Etapas, CancellationToken.None));
        Assert.Equal(c.Etapas["Venda concluída (Leads)"], lead.EtapaId);
    }

    [Theory]
    [InlineData("Perdido")]
    [InlineData("Não fazemos")]
    [InlineData("Venda concluída (Leads)")]
    public async Task PerdidoNaoFazemosEJaConcluido_NaoMudam(string coluna)
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, coluna);

        Assert.False(await c.Servico.GarantirColunaDeVendaAsync(lead, false, c.Etapas, CancellationToken.None));
        Assert.Equal(c.Etapas[coluna], lead.EtapaId);
    }

    [Fact]
    public async Task SemVendaOuComVendaExcluida_OLeadFicaOndeEsta()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var semVenda = await LeadAsync(c, "Cotação", comVenda: false);
        var excluida = await LeadAsync(c, "Cotação", vendaExcluida: true);

        Assert.False(await c.Servico.GarantirColunaDeVendaAsync(semVenda, false, c.Etapas, CancellationToken.None));
        Assert.False(await c.Servico.GarantirColunaDeVendaAsync(excluida, false, c.Etapas, CancellationToken.None));
        Assert.Equal(c.Etapas["Cotação"], semVenda.EtapaId);
        Assert.Equal(c.Etapas["Cotação"], excluida.EtapaId);
    }

    [Fact]
    public async Task VendaAindaNaoSalva_TambemConta()
    {
        using var factory = new TestDbContextFactory();
        var c = await MontarAsync(factory);
        var lead = await LeadAsync(c, "Cotação", comVenda: false);

        Assert.True(await c.Servico.GarantirColunaDeVendaAsync(lead, true, c.Etapas, CancellationToken.None));
        Assert.Equal(c.Etapas["Venda concluída (Leads)"], lead.EtapaId);
    }
}
