using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Authorization;
using CssVision.Web.Services.Crm;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>O mesmo aviso de pagamento em aberto não é criado duas vezes para o mesmo consultor (clique duplo, envio repetido).</summary>
public class AvisoPagamentoDuplicadoTests
{
    [Fact]
    public async Task AvisoIgualJaEmAberto_NaoCriaOutro_ELembrarEComReenviar()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        await factory.SeedRolesAsync(db);
        var ana = await factory.CriarUsuarioAsync(db, "Ana");
        var admin = await factory.CriarUsuarioAsync(db, "Admin");
        await factory.AtribuirPapelAsync(db, ana, Roles.Comercial);
        await factory.AtribuirPapelAsync(db, admin, Roles.Admin);

        var eu = TestDbContextFactory.MockCurrentUser(admin.Id, visaoTotal: true);
        eu.Setup(m => m.IsInRole(Roles.Admin)).Returns(true);
        var push = new Mock<IPushService>();
        push.Setup(p => p.EnviarAsync(It.IsAny<Guid>(), It.IsAny<PushMensagem>(), It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var servico = new AvisosPagamentoService(db, new EquipeComercialService(db, eu.Object), eu.Object, push.Object, new NoOpAuditSink(), NullLogger<AvisosPagamentoService>.Instance);
        var pedido = new AvisoPagamentoCreateRequest([ana.Id], "O coworking de outubro está em aberto.", 100m, "Coworking");

        await servico.EnviarAsync(pedido, CancellationToken.None);
        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() => servico.EnviarAsync(pedido, CancellationToken.None));

        Assert.Equal("aviso_duplicado", erro.Codigo);
        Assert.Contains("Reenviar", erro.Message);
        Assert.Equal(1, await db.CrmAvisosConsultor.CountAsync(a => a.ConsultorId == ana.Id));

        // Outro valor ou outro texto é outro aviso.
        await servico.EnviarAsync(pedido with { Valor = 150m }, CancellationToken.None);
        Assert.Equal(2, await db.CrmAvisosConsultor.CountAsync(a => a.ConsultorId == ana.Id));

        // Resolvido o primeiro, o mesmo texto pode ser enviado de novo.
        var primeiro = await db.CrmAvisosConsultor.OrderBy(a => a.CriadoEm).FirstAsync();
        await servico.ResolverAsync(primeiro.Id, CancellationToken.None);
        await servico.EnviarAsync(pedido, CancellationToken.None);
        Assert.Equal(3, await db.CrmAvisosConsultor.CountAsync(a => a.ConsultorId == ana.Id));
    }
}
