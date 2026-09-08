using System.Security.Cryptography;
using System.Text;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Marketing;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class MetaConversionEventBuilderTests
{
    private static string Sha256(string valor) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(valor)));

    [Theory]
    [InlineData("Joao@Teste.com", "joao@teste.com")]
    [InlineData("  maria@teste.com  ", "maria@teste.com")]
    public void HashEmail_DeveNormalizarAntesDeHashear(string entrada, string normalizado)
    {
        Assert.Equal(Sha256(normalizado), MetaConversionEventBuilder.HashEmail(entrada));
    }

    [Fact]
    public void HashEmail_DeveRetornarNulo_QuandoVazio()
    {
        Assert.Null(MetaConversionEventBuilder.HashEmail(null));
        Assert.Null(MetaConversionEventBuilder.HashEmail("  "));
    }

    [Theory]
    [InlineData("11988887777", "5511988887777")]
    [InlineData("(11) 98888-7777", "5511988887777")]
    [InlineData("5511988887777", "5511988887777")]
    public void HashPhone_DeveGarantirDDI55(string entrada, string comDdi)
    {
        Assert.Equal(Sha256(comDdi), MetaConversionEventBuilder.HashPhone(entrada));
    }

    [Fact]
    public void HashPhone_DeveRetornarNulo_QuandoVazio()
    {
        Assert.Null(MetaConversionEventBuilder.HashPhone(null));
    }

    [Fact]
    public void BuildVendaGanhaPayload_DeveMontarEventoCustomizadoEPurchase()
    {
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "Cliente",
            TipoPessoa = TipoPessoa.Fisica,
            EmailNormalizado = "cliente@teste.com",
            WhatsApp = "11988887777",
            MetaLeadId = "2141279250136854",
        };
        var opportunity = new CrmOpportunity
        {
            Id = Guid.NewGuid(),
            LeadId = lead.Id,
            Titulo = "Venda",
            ValorFinal = 150.00m,
            DataEfetivaFechamento = DateTimeOffset.UtcNow,
        };
        var options = new MetaCapiOptions { PixelId = "123", AccessToken = "token", EventoCustomizadoNome = "LeadConvertido", EventSourceLabel = "CssVision CRM" };

        var payload = MetaConversionEventBuilder.BuildVendaGanhaPayload(lead, opportunity, options);

        Assert.Equal(2, payload.Data.Count);

        var customizado = payload.Data[0];
        Assert.Equal("LeadConvertido", customizado.EventName);
        Assert.Equal($"custom_{opportunity.Id}", customizado.EventId);
        Assert.Equal("system_generated", customizado.ActionSource);
        Assert.Equal(150.00m, customizado.CustomData.Value);
        Assert.Equal("BRL", customizado.CustomData.Currency);
        Assert.Equal(2141279250136854L, customizado.UserData.LeadId);
        Assert.Equal(Sha256("cliente@teste.com"), Assert.Single(customizado.UserData.Em!));
        Assert.Equal(Sha256("5511988887777"), Assert.Single(customizado.UserData.Ph!));

        var purchase = payload.Data[1];
        Assert.Equal("Purchase", purchase.EventName);
        Assert.Equal($"purchase_{opportunity.Id}", purchase.EventId);
        Assert.Equal(150.00m, purchase.CustomData.Value);
    }

    [Fact]
    public void BuildVendaGanhaPayload_DeveOmitirLeadId_QuandoLeadNaoVeioDoMetaAds()
    {
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, EmailNormalizado = "cliente@teste.com" };
        var opportunity = new CrmOpportunity { Id = Guid.NewGuid(), LeadId = lead.Id, Titulo = "Venda", ValorFinal = 100m };
        var options = new MetaCapiOptions { PixelId = "123", AccessToken = "token" };

        var payload = MetaConversionEventBuilder.BuildVendaGanhaPayload(lead, opportunity, options);

        Assert.Null(payload.Data[0].UserData.LeadId);
        Assert.Null(payload.Data[0].UserData.Ph);
    }

    [Fact]
    public void BuildEtapaEventPayload_DeveNomearEventoComANomeDaEtapa()
    {
        var lead = new CrmLead { Id = Guid.NewGuid(), NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, EmailNormalizado = "cliente@teste.com" };
        var etapaId = Guid.NewGuid();
        var options = new MetaCapiOptions { PixelId = "123", AccessToken = "token" };

        var payload = MetaConversionEventBuilder.BuildEtapaEventPayload(lead, etapaId, "Cotação", options);

        var evento = Assert.Single(payload.Data);
        Assert.Equal("Cotação", evento.EventName);
        Assert.Equal($"etapa_{lead.Id}_{etapaId}", evento.EventId);
        Assert.Equal("system_generated", evento.ActionSource);
        Assert.Null(evento.CustomData.Value);
        Assert.Null(evento.CustomData.Currency);
        Assert.Equal(Sha256("cliente@teste.com"), Assert.Single(evento.UserData.Em!));
    }

    [Fact]
    public void BuildEtapaEventPayload_DeveGerarEventIdEstavel_ParaMesmaEtapaEMesmoLead()
    {
        var lead = new CrmLead { Id = Guid.NewGuid(), NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica };
        var etapaId = Guid.NewGuid();
        var options = new MetaCapiOptions { PixelId = "123", AccessToken = "token" };

        var payload1 = MetaConversionEventBuilder.BuildEtapaEventPayload(lead, etapaId, "Venda concluída", options);
        var payload2 = MetaConversionEventBuilder.BuildEtapaEventPayload(lead, etapaId, "Venda concluída", options);

        Assert.Equal(payload1.Data[0].EventId, payload2.Data[0].EventId);
    }
}
