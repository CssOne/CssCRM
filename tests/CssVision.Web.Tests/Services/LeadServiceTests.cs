using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class LeadServiceTests
{
    private static LeadCreateRequest NovoLeadRequest(
        string nome = "Cliente Teste", string? documento = "52998224725", string? email = "cliente@teste.com", string? telefone = "11988887777") =>
        new(
            NomeOuRazaoSocial: nome,
            TipoPessoa: TipoPessoa.Fisica,
            Documento: documento,
            Telefone: telefone,
            WhatsApp: null,
            Email: email,
            DataNascimento: null,
            Cidade: "São Paulo",
            Estado: "SP",
            Regional: null,
            Origem: "Site",
            Campanha: null,
            ProdutoInteresse: null,
            Gclid: null,
            UtmMedium: null,
            UtmSource: null,
            UtmTerm: null,
            MetaClickId: null,
            MetaFormId: null,
            MetaLeadId: null,
            IndicadoPorLeadId: null,
            TipoIndicacao: null,
            ResponsavelId: null,
            EtapaId: null,
            Tags: null,
            Observacoes: null,
            ConsentimentoContato: true,
            ConsentimentoOrigem: null);

    [Fact]
    public async Task CriarAsync_DeveCadastrarLead_QuandoDadosValidos()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");

        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var equipe = new EquipeComercialService(db, currentUser.Object);
        var service = new LeadService(db, currentUser.Object, equipe, new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        var resultado = await service.CriarAsync(NovoLeadRequest(), CancellationToken.None);

        Assert.Null(resultado.Duplicidade);
        Assert.NotNull(resultado.Lead);
        Assert.Equal("Cliente Teste", resultado.Lead!.NomeOuRazaoSocial);
        Assert.Equal(vendedor.Id, resultado.Lead.ResponsavelId);
    }

    [Fact]
    public async Task CriarAsync_DeveRejeitar_QuandoCpfInvalido()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        var request = NovoLeadRequest(documento: "11111111111");

        await Assert.ThrowsAsync<CrmBusinessException>(() => service.CriarAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CriarAsync_DeveDetectarDuplicidade_PorCpf()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        await service.CriarAsync(NovoLeadRequest("Primeiro Cliente"), CancellationToken.None);
        var segundo = await service.CriarAsync(NovoLeadRequest("Segundo Cliente", email: "outro@teste.com"), CancellationToken.None);

        Assert.Null(segundo.Lead);
        Assert.NotNull(segundo.Duplicidade);
        Assert.Equal("CPF/CNPJ", segundo.Duplicidade!.CampoDuplicado);
    }

    [Fact]
    public async Task CriarAsync_DeveDetectarDuplicidade_PorEmail()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var currentUser = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var service = new LeadService(db, currentUser.Object, new EquipeComercialService(db, currentUser.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        await service.CriarAsync(NovoLeadRequest("Primeiro Cliente", documento: null), CancellationToken.None);
        var segundo = await service.CriarAsync(NovoLeadRequest("Segundo Cliente", documento: null), CancellationToken.None);

        Assert.Null(segundo.Lead);
        Assert.Equal("e-mail", segundo.Duplicidade!.CampoDuplicado);
    }

    [Fact]
    public async Task ObterPorIdAsync_DeveNegarAcesso_QuandoLeadNaoPertenceAoVendedor()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor1 = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var vendedor2 = await factory.CriarUsuarioAsync(db, "Vendedor2");

        var currentUser1 = TestDbContextFactory.MockCurrentUser(vendedor1.Id);
        var service1 = new LeadService(db, currentUser1.Object, new EquipeComercialService(db, currentUser1.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        var criado = await service1.CriarAsync(NovoLeadRequest(), CancellationToken.None);

        var currentUser2 = TestDbContextFactory.MockCurrentUser(vendedor2.Id);
        var service2 = new LeadService(db, currentUser2.Object, new EquipeComercialService(db, currentUser2.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        await Assert.ThrowsAsync<CrmForbiddenException>(() => service2.ObterPorIdAsync(criado.Lead!.Id, CancellationToken.None));
    }

    [Fact]
    public async Task ListarAsync_DeveIsolarCarteira_EntreVendedores()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor1 = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var vendedor2 = await factory.CriarUsuarioAsync(db, "Vendedor2");

        var currentUser1 = TestDbContextFactory.MockCurrentUser(vendedor1.Id);
        var service1 = new LeadService(db, currentUser1.Object, new EquipeComercialService(db, currentUser1.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        await service1.CriarAsync(NovoLeadRequest(), CancellationToken.None);

        var currentUser2 = TestDbContextFactory.MockCurrentUser(vendedor2.Id);
        var service2 = new LeadService(db, currentUser2.Object, new EquipeComercialService(db, currentUser2.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        var listaVendedor2 = await service2.ListarAsync(new LeadFilterRequest(), CancellationToken.None);

        Assert.Empty(listaVendedor2.Itens);
    }

    [Fact]
    public async Task ListarAsync_GestorComercial_DeveVerCarteiraDaEquipe()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var gestor = await factory.CriarUsuarioAsync(db, "Gestor1");
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor1", gestorId: gestor.Id);

        var currentUserVendedor = TestDbContextFactory.MockCurrentUser(vendedor.Id);
        var serviceVendedor = new LeadService(db, currentUserVendedor.Object, new EquipeComercialService(db, currentUserVendedor.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        await serviceVendedor.CriarAsync(NovoLeadRequest(), CancellationToken.None);

        var currentUserGestor = TestDbContextFactory.MockCurrentUser(gestor.Id, gestorComercial: true);
        var serviceGestor = new LeadService(db, currentUserGestor.Object, new EquipeComercialService(db, currentUserGestor.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        var listaGestor = await serviceGestor.ListarAsync(new LeadFilterRequest(), CancellationToken.None);

        Assert.Single(listaGestor.Itens);
    }

    [Fact]
    public async Task ListarAsync_Admin_DeveVerVisaoConsolidada()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor1 = await factory.CriarUsuarioAsync(db, "Vendedor1");
        var vendedor2 = await factory.CriarUsuarioAsync(db, "Vendedor2");

        var currentUser1 = TestDbContextFactory.MockCurrentUser(vendedor1.Id);
        await new LeadService(db, currentUser1.Object, new EquipeComercialService(db, currentUser1.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink())
            .CriarAsync(NovoLeadRequest("Cliente A", documento: "52998224725", email: "a@teste.com", telefone: "11988880001"), CancellationToken.None);

        var currentUser2 = TestDbContextFactory.MockCurrentUser(vendedor2.Id);
        await new LeadService(db, currentUser2.Object, new EquipeComercialService(db, currentUser2.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink())
            .CriarAsync(NovoLeadRequest("Cliente B", documento: "11144477735", email: "b@teste.com", telefone: "11988880002"), CancellationToken.None);

        var admin = await factory.CriarUsuarioAsync(db, "AdminUser");
        var currentUserAdmin = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var serviceAdmin = new LeadService(db, currentUserAdmin.Object, new EquipeComercialService(db, currentUserAdmin.Object), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        var lista = await serviceAdmin.ListarAsync(new LeadFilterRequest(), CancellationToken.None);

        Assert.Equal(2, lista.TotalRegistros);
    }
}
