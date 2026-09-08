using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class PublicLeadIntakeServiceTests
{
    private static PublicLeadCreateRequest NovoRequest(
        string nome = "Cliente Site", string? whatsapp = "31988887777", string? email = "cliente@site.com") =>
        new(nome, whatsapp, email, "MG", "ABC1D23", "Onix 2020", null, null, null, null, "Facebook ADS", "UGC VENDA", "AGV", "agv3");

    [Fact]
    public async Task CriarAsync_DeveAtribuirViaRodizio_QuandoHaVendedorComercial()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        vendedor.FotoUrl = "/uploads/consultores/vendedor1.jpg";
        await db.SaveChangesAsync();
        await factory.AtribuirPapelAsync(db, vendedor, Roles.Comercial);

        var service = new PublicLeadIntakeService(db, new LeadAssignmentService(db), NullLogger<PublicLeadIntakeService>.Instance);
        var resultado = await service.CriarAsync(NovoRequest(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, resultado.LeadId);
        Assert.Equal("Vendedor1", resultado.ConsultorNome);
        Assert.Equal("/uploads/consultores/vendedor1.jpg", resultado.ConsultorFotoUrl);

        var lead = await db.CrmLeads.FindAsync(resultado.LeadId);
        Assert.NotNull(lead);
        Assert.Equal(vendedor.Id, lead!.ResponsavelId);
        Assert.Null(lead.EtapaId);
        Assert.Equal("agv3", lead.MetaFormId);
        Assert.Contains("Placa: ABC1D23", lead.Observacoes);
    }

    [Fact]
    public async Task CriarAsync_DeveDevolverConsultorNulo_QuandoNaoHaVendedorElegivel()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();

        var service = new PublicLeadIntakeService(db, new LeadAssignmentService(db), NullLogger<PublicLeadIntakeService>.Instance);
        var resultado = await service.CriarAsync(NovoRequest(), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, resultado.LeadId);
        Assert.Null(resultado.ConsultorNome);
        Assert.Null(resultado.ConsultorFotoUrl);
    }

    [Fact]
    public async Task CriarAsync_DeveDevolverMesmoConsultor_QuandoMesmoWhatsAppReenvia()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        await factory.AtribuirPapelAsync(db, vendedor, Roles.Comercial);

        var service = new PublicLeadIntakeService(db, new LeadAssignmentService(db), NullLogger<PublicLeadIntakeService>.Instance);
        var primeiro = await service.CriarAsync(NovoRequest(), CancellationToken.None);
        var segundo = await service.CriarAsync(NovoRequest(nome: "Cliente Site (reenvio)"), CancellationToken.None);

        Assert.Equal(primeiro.LeadId, segundo.LeadId);
        Assert.Equal(primeiro.ConsultorNome, segundo.ConsultorNome);
        Assert.Equal(1, await db.CrmLeads.CountAsync());
    }
}
