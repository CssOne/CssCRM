using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Aviso ao consultor das parcelas dos custos que o financeiro lançou: alguns dias antes do vencimento e no dia, uma vez cada.</summary>
public class CustoVencimentoTests
{
    private static readonly DateOnly Primeira = new(2026, 11, 10);

    private static async Task<(Guid ConsultorId, CrmCustoConsultor Custo)> CenarioAsync(TestDbContextFactory factory, ApplicationDbContext db)
    {
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var financeiro = await factory.CriarUsuarioAsync(db, "Financeiro");
        var custo = new CrmCustoConsultor
        {
            UsuarioId = ana.Id, Tipo = TipoCustoConsultor.Notebook, Descricao = "Notebook Dell", ValorTotal = 1200m, Parcelas = 12, PrimeiraParcela = Primeira, CriadoPorId = financeiro.Id,
        };
        db.CrmCustosConsultor.Add(custo);
        await db.SaveChangesAsync();
        return (ana.Id, custo);
    }

    private static (CustoVencimentoNotificador Servico, Mock<IPushService> Push) Notificador(ApplicationDbContext db)
    {
        var push = new Mock<IPushService>();
        push.Setup(p => p.EnviarAsync(It.IsAny<Guid>(), It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new CustoVencimentoNotificador(db, push.Object, NullLogger<CustoVencimentoNotificador>.Instance), push);
    }

    [Fact]
    public async Task Antecedencia_AvisaOConsultor3DiasAntes_UmaVezSo_ComPush()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, custo) = await CenarioAsync(factory, db);
        var (servico, push) = Notificador(db);

        Assert.Equal(0, await servico.NotificarAsync(Primeira.AddDays(-4), CancellationToken.None));   // ainda longe
        Assert.Equal(1, await servico.NotificarAsync(Primeira.AddDays(-3), CancellationToken.None));
        Assert.Equal(0, await servico.NotificarAsync(Primeira.AddDays(-2), CancellationToken.None));   // a antecedência já foi enviada

        var aviso = await db.CrmAvisosConsultor.SingleAsync();
        Assert.Equal(ana, aviso.ConsultorId);
        Assert.Equal("Parcela a vencer", aviso.Titulo);
        Assert.Contains("parcela 1/12 de Notebook Dell vence em 3 dias", aviso.Mensagem);
        Assert.Equal(100m, aviso.Valor);
        Assert.Equal("Notebook", aviso.Referencia);
        Assert.Equal(custo.CriadoPorId, aviso.EnviadoPorId);
        push.Verify(p => p.EnviarAsync(ana, It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NoDiaDoVencimento_AvisaDeNovo_EDepoisNaoRepete()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await CenarioAsync(factory, db);
        var (servico, _) = Notificador(db);

        await servico.NotificarAsync(Primeira.AddDays(-1), CancellationToken.None);
        Assert.Equal(1, await servico.NotificarAsync(Primeira, CancellationToken.None));
        Assert.Equal(0, await servico.NotificarAsync(Primeira, CancellationToken.None));
        Assert.Equal(0, await servico.NotificarAsync(Primeira.AddDays(1), CancellationToken.None));   // vencida: já não avisa

        var titulos = await db.CrmAvisosConsultor.OrderBy(a => a.CriadoEm).Select(a => a.Titulo).ToListAsync();
        Assert.Equal(["Parcela a vencer", "Parcela vence hoje"], titulos);
    }

    [Fact]
    public async Task ParcelaPaga_NaoGeraAviso()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, custo) = await CenarioAsync(factory, db);
        db.CrmCobrancasPagamento.Add(new CrmCobrancaPagamento { UsuarioId = ana, Item = ItemCobranca.Adicional, CustoId = custo.Id, Competencia = new DateOnly(2026, 11, 1), Pago = true, PagoEm = Primeira.AddDays(-5) });
        await db.SaveChangesAsync();
        var (servico, push) = Notificador(db);

        Assert.Equal(0, await servico.NotificarAsync(Primeira.AddDays(-2), CancellationToken.None));
        Assert.Equal(0, await servico.NotificarAsync(Primeira, CancellationToken.None));
        push.Verify(p => p.EnviarAsync(It.IsAny<Guid>(), It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ParcelasSeguintes_VencemNoMesmoDiaDosMesesSeguintes_ComAjusteNoFimDoMes()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (_, custo) = await CenarioAsync(factory, db);
        custo.PrimeiraParcela = new DateOnly(2026, 1, 31);
        await db.SaveChangesAsync();
        var (servico, _) = Notificador(db);

        Assert.Equal(new DateOnly(2026, 2, 28), CobrancasCalculo.VencimentoDaParcela(custo, 2));   // fevereiro não tem dia 31
        Assert.Equal(new DateOnly(2026, 3, 31), CobrancasCalculo.VencimentoDaParcela(custo, 3));
        Assert.Equal(1, await servico.NotificarAsync(new DateOnly(2026, 2, 28), CancellationToken.None));   // parcela 2 vence hoje
    }

    [Fact]
    public async Task ConsultorInativo_NaoRecebeAviso()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var (ana, _) = await CenarioAsync(factory, db);
        var usuario = await db.Users.SingleAsync(u => u.Id == ana);
        usuario.Ativo = false;
        await db.SaveChangesAsync();
        var (servico, _) = Notificador(db);

        Assert.Equal(0, await servico.NotificarAsync(Primeira, CancellationToken.None));
    }
}
