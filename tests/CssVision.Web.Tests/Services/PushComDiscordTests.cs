using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Discord;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Todo aviso por push passa a chegar também no Discord de quem vinculou a conta, sem nunca atrapalhar o push.</summary>
public class PushComDiscordTests
{
    private sealed class DiscordFalso(bool entrega = true, Exception? falha = null) : IDiscordService
    {
        public List<(Guid UsuarioId, string Titulo, string? Url)> Avisos { get; } = [];

        public Task<bool> EnviarAvisoAsync(Guid usuarioId, string titulo, string corpo, string? urlRelativa, CancellationToken ct)
        {
            Avisos.Add((usuarioId, titulo, urlRelativa));
            return falha is null ? Task.FromResult(entrega) : throw falha;
        }

        public Task<DiscordStatusDto> ObterStatusAsync(Guid usuarioId, CancellationToken ct) => throw new NotSupportedException();
        public Task<string> IniciarVinculoAsync(Guid usuarioId, string redirectUri, CancellationToken ct) => throw new NotSupportedException();
        public Task<DiscordStatusDto> ConcluirVinculoAsync(string code, string state, string redirectUri, CancellationToken ct) => throw new NotSupportedException();
        public Task DesvincularAsync(Guid usuarioId, CancellationToken ct) => throw new NotSupportedException();
        public Task DefinirAvisosAsync(Guid usuarioId, bool ativos, CancellationToken ct) => throw new NotSupportedException();
    }

    private static PushComDiscordService Montar(TestDbContextFactory factory, IDiscordService discord)
    {
        var db = factory.CreateContext();
        return new PushComDiscordService(new PushService(db, NullLogger<PushService>.Instance), discord, NullLogger<PushComDiscordService>.Instance);
    }

    [Fact]
    public async Task AvisoPorPush_TambemVaiParaODiscord_ESomaNaContagem()
    {
        using var factory = new TestDbContextFactory();
        var discord = new DiscordFalso(entrega: true);
        var servico = Montar(factory, discord);
        var usuario = Guid.NewGuid();

        // Sem navegador inscrito o push entrega 0; o Discord entrega 1.
        var entregues = await servico.EnviarAsync(usuario, new PushMensagem("Novo lead para você", "Maria", "/app/crm/leads/kanban", "lead-1"), CancellationToken.None);

        Assert.Equal(1, entregues);
        var aviso = Assert.Single(discord.Avisos);
        Assert.Equal(usuario, aviso.UsuarioId);
        Assert.Equal("Novo lead para você", aviso.Titulo);
        Assert.Equal("/app/crm/leads/kanban", aviso.Url);
    }

    [Fact]
    public async Task QuemNaoVinculouOuDesligou_NaoRecebeNoDiscord_EOPushSegueIgual()
    {
        using var factory = new TestDbContextFactory();
        var servico = Montar(factory, new DiscordFalso(entrega: false));

        Assert.Equal(0, await servico.EnviarAsync(Guid.NewGuid(), new PushMensagem("t", "c", "/app", "x"), CancellationToken.None));
    }

    [Fact]
    public async Task FalhaNoDiscord_NuncaDerrubaOAviso()
    {
        using var factory = new TestDbContextFactory();
        var servico = Montar(factory, new DiscordFalso(falha: new HttpRequestException("Discord fora do ar")));

        var entregues = await servico.EnviarAsync(Guid.NewGuid(), new PushMensagem("t", "c", "/app", "x"), CancellationToken.None);

        Assert.Equal(0, entregues); // não lançou
    }
}
