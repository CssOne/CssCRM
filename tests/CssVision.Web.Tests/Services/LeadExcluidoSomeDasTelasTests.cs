using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Lead excluído no quadro (lixeira) some de todas as outras telas — até com "inclui arquivados"; arquivado por outros meios continua aparecendo nesse filtro.</summary>
public class LeadExcluidoSomeDasTelasTests
{
    [Fact]
    public async Task Excluido_SaiDaListaDoQuadroEDasAtividades_MasOArquivadoPorOutroMeioContinuaNoFiltroDeArquivados()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var etapa = await factory.CriarEtapaAsync(db, "Novo", 1);
        CrmLead Lead(string nome) => new() { NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = admin.Id };
        var ativo = Lead("Ativo"); var excluido = Lead("Excluido pelo CRM"); var arquivadoNaMao = Lead("Arquivado por script");
        arquivadoNaMao.Arquivado = true; arquivadoNaMao.ArquivadoEm = DateTimeOffset.UtcNow;
        db.CrmLeads.AddRange(ativo, excluido, arquivadoNaMao);
        await db.SaveChangesAsync();
        db.CrmActivities.AddRange(
            new CrmActivity { LeadId = ativo.Id, ResponsavelId = admin.Id, Assunto = "Ligar ativo", DataHoraPrevista = DateTimeOffset.UtcNow },
            new CrmActivity { LeadId = excluido.Id, ResponsavelId = admin.Id, Assunto = "Ligar excluido", DataHoraPrevista = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true);
        var leads = new LeadService(db, usuario.Object, new EquipeComercialService(db, usuario.Object),
            new NoOpLeadAssignmentService(), new NoOpMetaConversionService(), new NoOpAuditSink());
        await leads.ExcluirAsync(excluido.Id, CancellationToken.None);
        var kanban = new LeadKanbanService(db, new EquipeComercialService(db, usuario.Object));
        var atividades = new ActivityService(db, usuario.Object, new EquipeComercialService(db, usuario.Object), new NoOpAuditSink());

        var lista = await leads.ListarAsync(new LeadFilterRequest { IncluirArquivados = true, Pagina = 1, TamanhoPagina = 50 }, CancellationToken.None);
        Assert.Equal(["Arquivado por script", "Ativo"], lista.Itens.Select(i => i.NomeOuRazaoSocial).Order().ToArray());

        var quadro = await kanban.ObterBoardAsync(new LeadKanbanFilterRequest { IncluirArquivados = true }, CancellationToken.None);
        Assert.DoesNotContain(quadro.Colunas.SelectMany(c => c.Cartoes), c => c.NomeOuRazaoSocial == "Excluido pelo CRM");

        var minhas = await atividades.ListarAsync(new ActivityFilterRequest { Visao = VisaoAtividade.Minhas, Pagina = 1, TamanhoPagina = 50 }, CancellationToken.None);
        Assert.Equal(["Ligar ativo"], minhas.Itens.Select(a => a.Assunto).ToArray());

        // E continua na lixeira.
        Assert.Equal(["Excluido pelo CRM"], (await leads.ListarLixeiraAsync(CancellationToken.None)).Select(l => l.NomeOuRazaoSocial).ToArray());
    }
}
