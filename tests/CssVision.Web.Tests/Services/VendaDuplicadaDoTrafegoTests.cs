using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Notion;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// Card de venda quase vazio do Notion (sem tipo, sem sinal de anúncio) que duplica a venda de um lead do tráfego pago (mesma placa, mesmo consultor, mesma época):
/// o lead do Notion é arquivado com a venda dele; o do tráfego fica. Casos parecidos mas diferentes não são mexidos.
/// </summary>
public class VendaDuplicadaDoTrafegoTests
{
    [Fact]
    public async Task ArquivaSoOLeadDoNotionQueDuplicaAVendaDoTrafego()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ricianne = await factory.CriarUsuarioAsync(db, "Ricianne");
        var outra = await factory.CriarUsuarioAsync(db, "Outra");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var venda = new DateTimeOffset(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);

        CrmLead Lead(string nome, string placa, Guid responsavel, string? origem, string? meta = null, bool manual = false, string? tipo = null) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = responsavel, Placa = placa, ConsentimentoOrigem = origem, MetaLeadId = meta,
            CriadoManualmente = manual, TipoIndicacao = tipo,
        };
        // O caso do erro: lead do tráfego (formulário do site, Meta) e o card vazio do Notion, "Indicação", com a mesma placa e o mesmo consultor.
        var trafego = Lead("Robson Cardoso Januario", "EYB4D42", ricianne.Id, OrigemLead.MarcadorFormularioSite, meta: "m1");
        var duplicado = Lead("ROBSON CARDOSO", "EYB4D42", ricianne.Id, OrigemLead.MarcadorSincronizacaoNotion, manual: true, tipo: "Indicação");
        // Parecidos, mas não são o mesmo caso: outro consultor; card do Notion com ID do Meta (tem sinal próprio); venda meses depois (outra venda).
        var trafegoB = Lead("Cliente B", "BBB1B11", ricianne.Id, OrigemLead.MarcadorFormularioSite, meta: "m2");
        var outroConsultor = Lead("Notion B", "BBB1B11", outra.Id, OrigemLead.MarcadorSincronizacaoNotion, manual: true, tipo: "Indicação");
        var trafegoC = Lead("Cliente C", "CCC1C11", ricianne.Id, OrigemLead.MarcadorFormularioSite, meta: "m3");
        var notionComMeta = Lead("Notion C", "CCC1C11", ricianne.Id, OrigemLead.MarcadorSincronizacaoNotion, meta: "m4");
        var trafegoD = Lead("Cliente D", "DDD1D11", ricianne.Id, OrigemLead.MarcadorFormularioSite, meta: "m5");
        var notionOutraEpoca = Lead("Notion D", "DDD1D11", ricianne.Id, OrigemLead.MarcadorSincronizacaoNotion, manual: true, tipo: "Indicação");
        db.CrmLeads.AddRange(trafego, duplicado, trafegoB, outroConsultor, trafegoC, notionComMeta, trafegoD, notionOutraEpoca);
        await db.SaveChangesAsync();

        void Venda(CrmLead l, Guid responsavel, DateTimeOffset data) => db.CrmOpportunities.Add(new CrmOpportunity
        {
            LeadId = l.Id, Titulo = l.NomeOuRazaoSocial, ResponsavelId = responsavel, EtapaId = ganho.Id, DataEfetivaFechamento = data, AtivoEm = data, PagamentoAdesao = 200m,
        });
        Venda(trafego, ricianne.Id, venda); Venda(duplicado, ricianne.Id, venda);
        Venda(trafegoB, ricianne.Id, venda); Venda(outroConsultor, outra.Id, venda);
        Venda(trafegoC, ricianne.Id, venda); Venda(notionComMeta, ricianne.Id, venda);
        Venda(trafegoD, ricianne.Id, venda); Venda(notionOutraEpoca, ricianne.Id, venda.AddDays(60));
        await db.SaveChangesAsync();

        var placas = await UnirVendasDuplicadasDoTrafegoBackgroundService.ExecutarAsync(db, CancellationToken.None);

        Assert.Equal(["EYB4D42"], placas);
        db.ChangeTracker.Clear();
        var leads = await db.CrmLeads.AsNoTracking().ToDictionaryAsync(l => l.NomeOuRazaoSocial);
        Assert.True(leads["ROBSON CARDOSO"].Arquivado);
        Assert.Null(leads["ROBSON CARDOSO"].ArquivadoPorId); // pelo sistema
        Assert.False(leads["Robson Cardoso Januario"].Arquivado);
        foreach (var nome in new[] { "Cliente B", "Notion B", "Cliente C", "Notion C", "Cliente D", "Notion D" }) Assert.False(leads[nome].Arquivado, nome);

        var vendas = await db.CrmOpportunities.AsNoTracking().ToDictionaryAsync(o => o.Titulo);
        Assert.True(vendas["ROBSON CARDOSO"].Arquivado);   // a venda duplicada deixa de contar
        Assert.False(vendas["Robson Cardoso Januario"].Arquivado);
    }

    [Fact]
    public async Task VendaVaziaDoLeadDeTrafego_NaoArquivaOCardDoNotionQueTemOsDadosReais()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var ricianne = await factory.CriarUsuarioAsync(db, "Ricianne");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var venda = new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero);
        var trafego = new CrmLead { NomeOuRazaoSocial = "Meta", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ricianne.Id, MetaLeadId = "m1", Placa = "STL1H88", TipoIndicacao = "Lead" };
        var card = new CrmLead
        {
            NomeOuRazaoSocial = "Notion", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ricianne.Id, Placa = "STL1H88", TipoIndicacao = "Indicação", CriadoManualmente = true,
            ConsentimentoOrigem = OrigemLead.MarcadorSincronizacaoNotion,
        };
        db.CrmLeads.AddRange(trafego, card);
        await db.SaveChangesAsync();
        db.CrmOpportunities.Add(new CrmOpportunity { LeadId = trafego.Id, Titulo = "vazia", ResponsavelId = ricianne.Id, EtapaId = ganho.Id, DataEfetivaFechamento = venda });
        db.CrmOpportunities.Add(new CrmOpportunity { LeadId = card.Id, Titulo = "real", ResponsavelId = ricianne.Id, EtapaId = ganho.Id, DataEfetivaFechamento = venda, PagamentoAdesao = 550m });
        await db.SaveChangesAsync();

        Assert.False(await DuplicidadeDeVendaDoNotion.ArquivarSeDuplicadoDeTrafegoAsync(db, card, CancellationToken.None));
        Assert.False((await db.CrmLeads.FirstAsync(l => l.Id == card.Id)).Arquivado);
    }
}
