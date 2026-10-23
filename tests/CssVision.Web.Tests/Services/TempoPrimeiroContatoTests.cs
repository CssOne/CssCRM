using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Gestão comercial: tempo médio até o primeiro contato (movimentação no quadro ou atividade concluída), só dos leads das últimas 24 horas.</summary>
public class TempoPrimeiroContatoTests
{
    [Fact]
    public async Task TempoMedio_ContaDaChegadaAtePrimeiraMovimentacaoOuAtividade()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var chegada = DateTimeOffset.UtcNow.AddHours(-20);
        var chegadaAntiga = DateTimeOffset.UtcNow.AddDays(-3);

        CrmLead Lead(string nome, string? origem = null) => new()
        {
            NomeOuRazaoSocial = nome, TipoPessoa = TipoPessoa.Fisica, ResponsavelId = ana.Id,
            MetaLeadId = Guid.NewGuid().ToString(), ConsentimentoOrigem = origem, CriadoEm = chegada,
        };
        var movido = Lead("Movido em 2h");
        var comAtividade = Lead("Atividade em 4h");
        var semContato = Lead("Sem contato");
        var doNotion = Lead("Do Notion", OrigemLead.MarcadorSincronizacaoNotion);
        var antigo = Lead("Chegou há 3 dias, fora da janela de 24 h");
        antigo.CriadoEm = chegadaAntiga;
        db.CrmLeads.AddRange(movido, comAtividade, semContato, doNotion, antigo);
        await db.SaveChangesAsync();
        // Ao salvar, o CRM marca o momento da atribuição; aqui os leads chegaram para a Ana há 20 horas (o antigo, há 3 dias).
        foreach (var lead in new[] { movido, comAtividade, semContato, doNotion }) lead.ResponsavelAtribuidoEm = chegada;
        antigo.ResponsavelAtribuidoEm = chegadaAntiga;
        await db.SaveChangesAsync();

        db.CrmAuditLogs.AddRange(
            new CrmAuditLog { UsuarioId = ana.Id, Acao = "LeadMudouEtapa", EntidadeTipo = nameof(CrmLead), EntidadeId = movido.Id, OcorridoEm = chegada.AddHours(2) },
            new CrmAuditLog { UsuarioId = ana.Id, Acao = "LeadMudouEtapa", EntidadeTipo = nameof(CrmLead), EntidadeId = movido.Id, OcorridoEm = chegada.AddHours(9) },
            new CrmAuditLog { UsuarioId = ana.Id, Acao = "LeadMudouEtapa", EntidadeTipo = nameof(CrmLead), EntidadeId = doNotion.Id, OcorridoEm = chegada.AddHours(1) },
            new CrmAuditLog { UsuarioId = ana.Id, Acao = "LeadMudouEtapa", EntidadeTipo = nameof(CrmLead), EntidadeId = antigo.Id, OcorridoEm = chegadaAntiga.AddHours(1) });
        db.CrmActivities.Add(new CrmActivity
        {
            LeadId = comAtividade.Id, Assunto = "Ligação", ResponsavelId = ana.Id,
            Status = StatusAtividade.Concluida, DataHoraConclusao = chegada.AddHours(4), DataHoraPrevista = chegada.AddHours(4),
        });
        await db.SaveChangesAsync();

        var usuario = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true, podeGerir: true).Object;
        var gestao = new ManagementService(db, usuario, new EquipeComercialService(db, usuario), TestDbContextFactory.CreateUserManager(db));
        var resumo = await gestao.ObterResumoAsync(null, null, CancellationToken.None);

        Assert.Equal(3.0, resumo.TempoMedioPrimeiroContatoHoras); // (2h + 4h) / 2 — o do Notion, o sem contato e o que chegou há 3 dias ficam fora
        Assert.Equal(2, resumo.LeadsComPrimeiroContato);
        var ana_ = Assert.Single(resumo.PrimeiroContatoPorVendedor!);
        Assert.Equal(ana.Id, ana_.VendedorId);
    }
}
