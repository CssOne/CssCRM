using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>
/// A data da venda é a do dia de Brasília em que ela aconteceu: venda de 30/09 à noite (já 1º/10 em UTC) é de setembro no quadro, na lista
/// e na meta — igual ao painel da TV. E só venda ganha e não excluída entra no filtro "Data da venda".
/// </summary>
public class FiltroDataDaVendaTests
{
    // 30/09 às 21h30 em Brasília = 01/10 às 00h30 em UTC.
    private static readonly DateTimeOffset VendaNoFimDeSetembro = new(2026, 10, 1, 0, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task QuadroELista_FiltramPeloDiaDeBrasilia_ESoVendaGanhaNaoExcluida()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 8, TipoEtapaPipeline.Ganho);
        var perdido = await factory.CriarEtapaAsync(db, "Perdido", 9, TipoEtapaPipeline.Perdido);

        CrmLead Lead(string nome) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin.Id };
        var noFim = Lead("Venda 30/09 noite"); var perda = Lead("Perdido em 30/09"); var excluida = Lead("Venda excluida");
        var duas = Lead("Agosto e outubro");
        db.CrmLeads.AddRange(noFim, perda, excluida, duas);
        await db.SaveChangesAsync();
        CrmOpportunity Op(CrmLead l, Guid etapa, DateTimeOffset data, bool arquivada = false) => new()
        {
            LeadId = l.Id, Titulo = "AGV", ResponsavelId = admin.Id, EtapaId = etapa, DataEfetivaFechamento = data, Arquivado = arquivada,
        };
        db.CrmOpportunities.AddRange(
            Op(noFim, ganho.Id, VendaNoFimDeSetembro),
            Op(perda, perdido.Id, new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero)),
            Op(excluida, ganho.Id, new DateTimeOffset(2026, 9, 30, 15, 0, 0, TimeSpan.Zero), arquivada: true),
            Op(duas, ganho.Id, new DateTimeOffset(2026, 8, 10, 15, 0, 0, TimeSpan.Zero)),
            Op(duas, ganho.Id, new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var kanban = new LeadKanbanService(db, new EquipeComercialService(db, usuario.Object));
        var leads = new LeadService(db, usuario.Object, new EquipeComercialService(db, usuario.Object),
            new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        async Task<string[]> NoQuadro(DateOnly? de, DateOnly? ate) =>
            (await kanban.ObterBoardAsync(new LeadKanbanFilterRequest { DataVendaInicio = de, DataVendaFim = ate }, CancellationToken.None))
                .Colunas.SelectMany(c => c.Cartoes).Select(c => c.NomeOuRazaoSocial).Order().ToArray();
        async Task<string[]> NaLista(DateOnly? de, DateOnly? ate) =>
            (await leads.ListarAsync(new LeadFilterRequest { DataVendaInicio = de, DataVendaFim = ate, Pagina = 1, TamanhoPagina = 50 }, CancellationToken.None))
                .Itens.Select(i => i.NomeOuRazaoSocial).Order().ToArray();

        foreach (var buscar in new Func<DateOnly?, DateOnly?, Task<string[]>>[] { NoQuadro, NaLista })
        {
            Assert.Equal(["Venda 30/09 noite"], await buscar(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30)));
            Assert.Empty(await buscar(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1))); // em UTC já seria 1º/10
            Assert.Equal(["Venda 30/09 noite"], await buscar(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30))); // nem perda, nem excluída, nem as duas vendas de fora
            Assert.Equal(["Agosto e outubro"], await buscar(new DateOnly(2026, 10, 2), null));
        }
    }

    [Fact]
    public async Task Filtro_UsaADataDeAtivacao_ESoCaiParaADataDaVendaQuandoNaoHaAtivacao()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 8, TipoEtapaPipeline.Ganho);
        CrmLead Lead(string nome) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin.Id };
        var ativadaDepois = Lead("Vendida 30/09, ativada 01/10"); var semAtivacao = Lead("Sem ativacao"); var ativadaAntes = Lead("Vendida 02/10, ativada 30/09");
        db.CrmLeads.AddRange(ativadaDepois, semAtivacao, ativadaAntes);
        await db.SaveChangesAsync();
        CrmOpportunity Op(CrmLead l, DateTimeOffset venda, DateTimeOffset? ativo) => new()
        {
            LeadId = l.Id, Titulo = "AGV", ResponsavelId = admin.Id, EtapaId = ganho.Id, DataEfetivaFechamento = venda, AtivoEm = ativo,
        };
        db.CrmOpportunities.AddRange(
            Op(ativadaDepois, new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)),
            Op(semAtivacao, new DateTimeOffset(2026, 10, 1, 15, 0, 0, TimeSpan.Zero), null),
            Op(ativadaAntes, new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        var kanban = new LeadKanbanService(db, new EquipeComercialService(db, usuario.Object));
        var leads = new LeadService(db, usuario.Object, new EquipeComercialService(db, usuario.Object),
            new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());

        async Task<string[]> NoQuadro(DateOnly de, DateOnly ate) =>
            (await kanban.ObterBoardAsync(new LeadKanbanFilterRequest { DataVendaInicio = de, DataVendaFim = ate }, CancellationToken.None))
                .Colunas.SelectMany(c => c.Cartoes).Select(c => c.NomeOuRazaoSocial).Order().ToArray();
        async Task<string[]> NaLista(DateOnly de, DateOnly ate) =>
            (await leads.ListarAsync(new LeadFilterRequest { DataVendaInicio = de, DataVendaFim = ate, Pagina = 1, TamanhoPagina = 50 }, CancellationToken.None))
                .Itens.Select(i => i.NomeOuRazaoSocial).Order().ToArray();

        foreach (var buscar in new Func<DateOnly, DateOnly, Task<string[]>>[] { NoQuadro, NaLista })
        {
            Assert.Equal(["Sem ativacao", "Vendida 30/09, ativada 01/10"], await buscar(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1)));
            Assert.Equal(["Vendida 02/10, ativada 30/09"], await buscar(new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30)));
            Assert.Empty(await buscar(new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 31)));
        }
    }

    [Fact]
    public async Task Meta_ContaAVendaDoFimDeSetembroEmSetembro_ENaoContaVendaExcluida()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        using var userManager = TestDbContextFactory.CreateUserManager(db);
        var consultor = await factory.CriarUsuarioAsync(db, "Consultor");
        await userManager.AddToRoleAsync(consultor, Roles.Comercial);
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ganho = await factory.CriarEtapaAsync(db, "Ganho", 9, TipoEtapaPipeline.Ganho);
        var lead = new CrmLead { NomeOuRazaoSocial = "Cliente", TipoPessoa = TipoPessoa.Fisica, ResponsavelId = consultor.Id };
        db.CrmLeads.Add(lead);
        await db.SaveChangesAsync();
        db.CrmOpportunities.AddRange(
            new CrmOpportunity { LeadId = lead.Id, Titulo = "V1", ResponsavelId = consultor.Id, EtapaId = ganho.Id, PagamentoAdesao = 100m, DataEfetivaFechamento = VendaNoFimDeSetembro },
            new CrmOpportunity { LeadId = lead.Id, Titulo = "V2", ResponsavelId = consultor.Id, EtapaId = ganho.Id, PagamentoAdesao = 50m, DataEfetivaFechamento = VendaNoFimDeSetembro, Arquivado = true });
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true);
        var service = new GoalService(db, usuario.Object, new EquipeComercialService(db, usuario.Object), userManager);

        var setembro = Assert.Single(await service.ListarAsync(new DateOnly(2026, 9, 1), CancellationToken.None));
        var outubro = Assert.Single(await service.ListarAsync(new DateOnly(2026, 10, 1), CancellationToken.None));
        Assert.Equal((1, 100m), (setembro.RealizadoQuantidade, setembro.RealizadoValor));
        Assert.Equal(0, outubro.RealizadoQuantidade);
    }
}
