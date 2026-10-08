using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// "Indicação Lead": lead que fechou por indicação vai para "Venda concluída (Indicação)" com etiqueta
/// própria. Veículos adicionais: cada veículo a mais do mesmo cliente vira um card novo.
/// </summary>
public class IndicacaoLeadEVeiculosAdicionaisTests
{
    private static LeadService LeadService(ApplicationDbContext db, ICurrentUserService usuario) =>
        new(db, usuario, new EquipeComercialService(db, usuario), new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

    private static async Task<(ApplicationDbContext Db, ICurrentUserService Usuario, CrmLead Lead)> PrepararAsync(
        TestDbContextFactory factory, string? tipoIndicacao = "Lead", CrmLeadStage? etapa = null)
    {
        var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var lead = new CrmLead
        {
            NomeOuRazaoSocial = "Cliente Frota",
            TipoPessoa = TipoPessoa.Fisica,
            ResponsavelId = vendedor.Id,
            TipoIndicacao = tipoIndicacao,
            CriadoManualmente = false,
            DocumentoNormalizado = "52998224725",
            Email = "frota@exemplo.com",
            EmailNormalizado = "frota@exemplo.com",
            Telefone = "(31) 99999-0000",
            TelefoneNormalizado = "5531999990000",
            Estado = "MG",
            MetaLeadId = "meta-123",
            EtapaId = etapa?.Id,
        };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        return (db, TestDbContextFactory.MockCurrentUser(vendedor.Id).Object, lead);
    }

    [Fact]
    public async Task LeadMovidoParaVendaConcluidaIndicacao_GanhaEtiquetaIndicacaoLead()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead) = await PrepararAsync(factory);
        var vendaIndicacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Venda concluída (Indicação)", 5);

        var detalhe = await LeadService(db, usuario).MudarEtapaAsync(lead.Id,
            new ChangeLeadStageRequest(vendaIndicacao.Id, lead.RowVersion), CancellationToken.None);

        Assert.Equal(vendaIndicacao.Id, detalhe.EtapaId);
        Assert.Equal("Indicação Lead", detalhe.TipoIndicacao);
    }

    [Theory]
    [InlineData("Venda concluída (Leads)", "Lead")]
    [InlineData("Venda concluída (Indicação)", "Pessoal")]
    public async Task OutrosCasos_NaoMudamOTipo(string coluna, string tipo)
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead) = await PrepararAsync(factory, tipo);
        var etapa = await factory.ObterOuCriarEtapaLeadAsync(db, coluna, 5);

        var detalhe = await LeadService(db, usuario).MudarEtapaAsync(lead.Id,
            new ChangeLeadStageRequest(etapa.Id, lead.RowVersion), CancellationToken.None);

        Assert.Equal(tipo, detalhe.TipoIndicacao);
    }

    [Theory]
    [InlineData("Indicação Lead", "Lead", "Indicação Lead")]
    [InlineData("Indicação Lead", null, "Indicação Lead")]
    [InlineData("Indicação Lead", "Pessoal", "Pessoal")]
    [InlineData("Lead", "Pessoal", "Pessoal")]
    [InlineData(null, "Lead", "Lead")]
    public void SincronizacaoDoNotion_NaoDesfazIndicacaoLead(string? atual, string? doNotion, string? esperado) =>
        Assert.Equal(esperado, TipoIndicacaoLead.ManterIndicacaoLead(atual, doNotion));

    [Fact]
    public async Task VeiculosAdicionais_CriaUmCardPorVeiculo_ComDadosECpfDoClienteSemPlacaNemRastreio()
    {
        using var factory = new TestDbContextFactory();
        var db0 = factory.CreateContext();
        var emAtendimento = await factory.ObterOuCriarEtapaLeadAsync(db0, "Em atendimento (Leads)", 1);
        var (db, usuario, lead) = await PrepararAsync(factory, etapa: emAtendimento);

        var ids = await LeadService(db, usuario).CriarVeiculosAdicionaisAsync(lead.Id, new LeadVeiculosAdicionaisRequest(null, 2), CancellationToken.None);

        Assert.Equal(2, ids.Count);
        db.ChangeTracker.Clear();
        var novos = await db.CrmLeads.AsNoTracking().Where(l => ids.Contains(l.Id)).ToListAsync();
        Assert.All(novos, n =>
        {
            Assert.Equal(lead.Id, n.VeiculoAdicionalDeLeadId);
            Assert.Equal("Cliente Frota", n.NomeOuRazaoSocial);
            Assert.Equal(lead.ResponsavelId, n.ResponsavelId);
            Assert.Equal("5531999990000", n.TelefoneNormalizado);
            Assert.Equal("Lead", n.TipoIndicacao);
            Assert.Equal(emAtendimento.Id, n.EtapaId);
            // Mesmo CPF/CNPJ e e-mail do cliente (um card por veículo), sem a placa do outro veículo.
            Assert.Equal("52998224725", n.DocumentoNormalizado);
            Assert.Equal("frota@exemplo.com", n.EmailNormalizado);
            Assert.Null(n.Placa);
            Assert.Null(n.MetaLeadId);
        });

        // A venda do card novo usa o CPF do card original.
        var detalhe = await LeadService(db, usuario).ObterPorIdAsync(ids[0], CancellationToken.None);
        Assert.Equal(lead.Id, detalhe.VeiculoAdicionalDeLeadId);
        Assert.Equal("529.982.247-25", detalhe.VeiculoAdicionalDeDocumento);
    }

    [Fact]
    public async Task VeiculosAdicionais_OriginalJaVendido_CardNovoVaiParaEmAtendimentoDaMesmaEtiqueta()
    {
        using var factory = new TestDbContextFactory();
        var db0 = factory.CreateContext();
        var emAtendimentoLeads = await factory.ObterOuCriarEtapaLeadAsync(db0, "Em atendimento (Leads)", 1);
        await factory.ObterOuCriarEtapaLeadAsync(db0, "Em atendimento (Indicação)", 2);
        var vendaLeads = await factory.ObterOuCriarEtapaLeadAsync(db0, "Venda concluída (Leads)", 5);
        vendaLeads.Fechada = true;
        await db0.SaveChangesAsync();
        var (db, usuario, lead) = await PrepararAsync(factory, etapa: vendaLeads);

        var ids = await LeadService(db, usuario).CriarVeiculosAdicionaisAsync(lead.Id, new LeadVeiculosAdicionaisRequest(null), CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(emAtendimentoLeads.Id, (await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == ids[0])).EtapaId);
    }

    [Theory]
    [InlineData("Lead", "Venda concluída (Leads)")]
    [InlineData("Indicação Lead", "Venda concluída (Indicação)")]
    [InlineData("Pessoal", "Venda concluída (Indicação)")]
    public async Task OutroVeiculo_ComVendaConcluida_CardNasceEmVendaConcluidaDaMesmaEtiqueta(string tipo, string colunaEsperada)
    {
        using var factory = new TestDbContextFactory();
        var db0 = factory.CreateContext();
        var emAtendimento = await factory.ObterOuCriarEtapaLeadAsync(db0, "Em atendimento (Leads)", 1);
        await factory.ObterOuCriarEtapaLeadAsync(db0, "Venda concluída (Leads)", 5);
        await factory.ObterOuCriarEtapaLeadAsync(db0, "Venda concluída (Indicação)", 6);
        var (db, usuario, lead) = await PrepararAsync(factory, tipo, emAtendimento);

        var ids = await LeadService(db, usuario).CriarVeiculosAdicionaisAsync(
            lead.Id, new LeadVeiculosAdicionaisRequest(null, 1, VendaConcluida: true), CancellationToken.None);

        db.ChangeTracker.Clear();
        var novo = await db.CrmLeads.AsNoTracking().Include(l => l.Etapa).SingleAsync(l => l.Id == ids[0]);
        Assert.Equal(colunaEsperada, novo.Etapa!.Nome);
        Assert.Equal(lead.Id, novo.VeiculoAdicionalDeLeadId);
        // O card do cliente não muda de coluna.
        Assert.Equal(emAtendimento.Id, (await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == lead.Id)).EtapaId);
    }

    [Fact]
    public async Task VeiculosAdicionais_APartirDeOutroAdicional_ApontaParaOCardOriginal()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead) = await PrepararAsync(factory);
        var service = LeadService(db, usuario);
        var primeiro = (await service.CriarVeiculosAdicionaisAsync(lead.Id, new LeadVeiculosAdicionaisRequest(null), CancellationToken.None))[0];

        var segundo = (await service.CriarVeiculosAdicionaisAsync(primeiro, new LeadVeiculosAdicionaisRequest(null), CancellationToken.None))[0];

        db.ChangeTracker.Clear();
        Assert.Equal(lead.Id, (await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == segundo)).VeiculoAdicionalDeLeadId);
    }

    [Fact]
    public async Task CardDeOutroVeiculo_PodeSerEditadoComOMesmoCpfDoCliente()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead) = await PrepararAsync(factory);
        var service = LeadService(db, usuario);
        var adicionalId = (await service.CriarVeiculosAdicionaisAsync(lead.Id, new LeadVeiculosAdicionaisRequest(null), CancellationToken.None))[0];
        db.ChangeTracker.Clear();
        var adicional = await db.CrmLeads.AsNoTracking().SingleAsync(l => l.Id == adicionalId);

        var salvo = await service.AtualizarAsync(adicionalId, new LeadUpdateRequest(
            "Cliente Frota", TipoPessoa.Fisica, "529.982.247-25", "(31) 99999-0000", null, null, "frota@exemplo.com", null, "Belo Horizonte", "MG", "MG132",
            null, null, null, "ABC1D23", null, null, null, null, null, null, null, null, null, null, "Lead", null,
            null, false, null, adicional.RowVersion), CancellationToken.None);

        Assert.Equal("529.982.247-25", salvo.Documento);
        Assert.Equal("ABC1D23", salvo.Placa);
    }

    [Fact]
    public async Task NovoClienteComCpfJaCadastrado_AvisaApontandoParaOCardPrincipal()
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead) = await PrepararAsync(factory);
        var service = LeadService(db, usuario);
        await service.CriarVeiculosAdicionaisAsync(lead.Id, new LeadVeiculosAdicionaisRequest(null, 2), CancellationToken.None);

        var resultado = await service.CriarAsync(new LeadCreateRequest(
            "Cliente Frota", TipoPessoa.Fisica, "529.982.247-25", "31999990000", null, null, null, null, "Belo Horizonte", "MG", "MG132", null, null, null, "ABC1D23",
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, false, null), CancellationToken.None);

        Assert.Null(resultado.Lead);
        Assert.Equal("CPF/CNPJ", resultado.Duplicidade!.CampoDuplicado);
        Assert.Equal(lead.Id, resultado.Duplicidade.LeadExistenteId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public async Task VeiculosAdicionais_QuantidadeForaDoLimite_ERecusada(int quantidade)
    {
        using var factory = new TestDbContextFactory();
        var (db, usuario, lead) = await PrepararAsync(factory);

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            LeadService(db, usuario).CriarVeiculosAdicionaisAsync(lead.Id, new LeadVeiculosAdicionaisRequest(null, quantidade), CancellationToken.None));

        Assert.Equal("quantidade_invalida", erro.Codigo);
    }
}
