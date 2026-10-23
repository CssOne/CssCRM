using CssVision.Web.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using CssVision.Web.Data;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Ao subir (fora de desenvolvimento) confere quantas contas ainda estão com a senha inicial antiga (<see cref="SenhaSegura.SenhaInicialLegada"/>, que esteve escrita no
/// código). O login já recusa essa senha, mas conta nessa situação é conta que ninguém reivindicou: o aviso no log lembra o administrador de redefinir ou desativar.
/// Só lê e registra — não altera nenhuma conta.
/// </summary>
public sealed class SenhaLegadaVerificador(IServiceScopeFactory scopeFactory, IHostEnvironment ambiente, ILogger<SenhaLegadaVerificador> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (ambiente.IsDevelopment()) return;
        try
        {
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<ApplicationUser>>();

            var contas = await db.Users.AsNoTracking().Where(u => u.Ativo && u.PasswordHash != null).ToListAsync(stoppingToken);
            var comSenhaLegada = contas
                .Where(u => hasher.VerifyHashedPassword(u, u.PasswordHash!, SenhaSegura.SenhaInicialLegada) != PasswordVerificationResult.Failed)
                .Select(u => u.Id)
                .ToList();

            if (comSenhaLegada.Count > 0)
            {
                logger.LogWarning("{Quantidade} conta(s) ativa(s) ainda estão com a senha inicial antiga (o login a recusa). Redefina a senha ou desative: {Ids}",
                    comSenhaLegada.Count, string.Join(", ", comSenhaLegada));
            }
            else
            {
                logger.LogInformation("Nenhuma conta ativa está com a senha inicial antiga.");
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Não foi possível conferir as contas com a senha inicial antiga.");
        }
    }
}
