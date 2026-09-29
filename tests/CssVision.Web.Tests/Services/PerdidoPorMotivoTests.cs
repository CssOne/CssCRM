using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Coluna "Perdido": motivo no cartão e filtro por motivo, somado aos filtros de cima.</summary>
public class PerdidoPorMotivoTests
{
    [Fact]
    public async Task ColunaPerdido_TrazMotivos_EFiltraPorMotivoRespeitandoOsFiltrosDeCima()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var vendedor = await factory.CriarUsuarioAsync(db, "Vendedor");
        var perdido = await factory.ObterOuCriarEtapaLeadAsync(db, "Perdido", 6);
        var cotacao = await factory.ObterOuCriarEtapaLeadAsync(db, "Cotação", 2);
        var numero = new CrmLossReason { Descricao = "Número não existe" };
        var tabela = new CrmLossReason { Descricao = "Fora da tabela de aceitação" };
        db.CrmLossReasons.AddRange(numero, tabela);

        CrmLead Lead(string nome, Guid etapa, CrmLossReason? motivo, string regional = "MG") => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = vendedor.Id, EtapaId = etapa,
            MotivoPerdaId = motivo?.Id, MotivoPerdaObservacao = motivo is null ? null : "ligou 3x", Regional = regional,
        };
        db.CrmLeads.AddRange(
            Lead("A", perdido.Id, numero),
            Lead("B", perdido.Id, numero, regional: "SP"),
            Lead("C", perdido.Id, tabela),
            Lead("D", perdido.Id, null),
            Lead("E", cotacao.Id, null));
        await db.SaveChangesAsync();

        var service = new LeadKanbanService(db, new EquipeComercialService(db, TestDbContextFactory.MockCurrentUser(vendedor.Id).Object));
        LeadKanbanColumnDto Coluna(LeadKanbanBoardDto b, string nome) => b.Colunas.Single(c => c.Etapa.Nome == nome);

        // Sem filtro de motivo: todos os perdidos e as opções com a quantidade.
        var board = await service.ObterBoardAsync(new LeadKanbanFilterRequest(), CancellationToken.None);
        var colunaPerdido = Coluna(board, "Perdido");
        Assert.Equal(4, colunaPerdido.Total);
        Assert.Contains(colunaPerdido.MotivosPerda!, m => m.Descricao == "Número não existe" && m.Quantidade == 2);
        Assert.Contains(colunaPerdido.MotivosPerda!, m => m.Id == Guid.Empty && m.Descricao == "Sem motivo informado");
        Assert.Equal("Número não existe", colunaPerdido.Cartoes.Single(c => c.NomeOuRazaoSocial == "A").MotivoPerda);

        // Filtro por motivo só afeta a coluna Perdido.
        var porNumero = await service.ObterBoardAsync(new LeadKanbanFilterRequest { MotivoPerdaId = [numero.Id] }, CancellationToken.None);
        Assert.Equal(2, Coluna(porNumero, "Perdido").Total);
        Assert.Equal(["A", "B"], Coluna(porNumero, "Perdido").Cartoes.Select(c => c.NomeOuRazaoSocial).Order());
        Assert.Equal(1, Coluna(porNumero, "Cotação").Total);

        // Soma-se aos filtros de cima (regional MG).
        var numeroEmMg = await service.ObterBoardAsync(
            new LeadKanbanFilterRequest { MotivoPerdaId = [numero.Id], Regional = ["MG"] }, CancellationToken.None);
        Assert.Equal("A", Assert.Single(Coluna(numeroEmMg, "Perdido").Cartoes).NomeOuRazaoSocial);
        Assert.Equal(1, Coluna(numeroEmMg, "Perdido").MotivosPerda!.Single(m => m.Id == numero.Id).Quantidade);

        // "Sem motivo informado".
        var semMotivo = await service.ObterBoardAsync(new LeadKanbanFilterRequest { MotivoPerdaId = [Guid.Empty] }, CancellationToken.None);
        Assert.Equal("D", Assert.Single(Coluna(semMotivo, "Perdido").Cartoes).NomeOuRazaoSocial);

        // "Ver mais" da coluna também respeita o motivo.
        var pagina = await service.ObterCartoesAsync(
            new LeadKanbanColunaRequest { EtapaId = perdido.Id, MotivoPerdaId = [tabela.Id] }, CancellationToken.None);
        Assert.Equal("C", Assert.Single(pagina).NomeOuRazaoSocial);
    }
}
