using CssVision.Web.Api.Contracts.Common;
using CssVision.Web.Data;
using CssVision.Web.Domain.Crm;
using CssVision.Web.Services.Backup;
using CssVision.Web.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CssVision.Web.Tests.Services;

/// <summary>Backup do banco: registro, contagens, download, falha e retenção dos 30 mais recentes.</summary>
public class BackupServiceTests
{
    private sealed class DumpFalso(bool falhar = false) : IDumpBanco
    {
        public Task<string> GerarAsync(CancellationToken ct)
        {
            if (falhar) throw new InvalidOperationException("pg_dump falhou: conexão recusada");
            var arquivo = Path.GetTempFileName();
            File.WriteAllText(arquivo, "conteudo do backup");
            return Task.FromResult(arquivo);
        }
    }

    private sealed class ArmazenamentoFalso : IArmazenamentoBackup
    {
        public Dictionary<string, byte[]> Arquivos { get; } = new();

        public Task SalvarAsync(string chave, string caminhoArquivo, CancellationToken ct)
        {
            Arquivos[chave] = File.ReadAllBytes(caminhoArquivo);
            return Task.CompletedTask;
        }

        public Task<Stream> AbrirAsync(string chave, CancellationToken ct) => Task.FromResult<Stream>(new MemoryStream(Arquivos[chave]));

        public Task ExcluirAsync(string chave, CancellationToken ct)
        {
            Arquivos.Remove(chave);
            return Task.CompletedTask;
        }
    }

    private static BackupService Service(ApplicationDbContext db, IArmazenamentoBackup armazenamento, bool falhar = false) =>
        new(db, new DumpFalso(falhar), armazenamento, new NoOpAuditSink(), NullLogger<BackupService>.Instance);

    [Fact]
    public async Task GerarBackup_GuardaOArquivo_ERegistraAsContagens()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        db.CrmLeads.AddRange(
            new CrmLead { NomeOuRazaoSocial = "A", TipoPessoa = TipoPessoa.Fisica },
            new CrmLead { NomeOuRazaoSocial = "B", TipoPessoa = TipoPessoa.Fisica });
        await db.SaveChangesAsync();
        var armazenamento = new ArmazenamentoFalso();

        var backup = await Service(db, armazenamento).GerarAsync(OrigemBackup.Manual, CancellationToken.None);

        Assert.Equal("Concluido", backup.Status);
        Assert.Equal(2, backup.Leads);
        Assert.True(backup.TamanhoBytes > 0);
        var chave = Assert.Single(armazenamento.Arquivos.Keys);
        Assert.StartsWith("backups/crm-", chave);

        var (conteudo, nome) = await Service(db, armazenamento).AbrirAsync(backup.Id, CancellationToken.None);
        using var leitor = new StreamReader(conteudo);
        Assert.Equal("conteudo do backup", await leitor.ReadToEndAsync());
        Assert.EndsWith(".dump", nome);
    }

    [Fact]
    public async Task FalhaNoDump_FicaRegistradaComoFalhou_ESemArquivo()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var armazenamento = new ArmazenamentoFalso();

        var erro = await Assert.ThrowsAsync<CrmBusinessException>(() =>
            Service(db, armazenamento, falhar: true).GerarAsync(OrigemBackup.Automatico, CancellationToken.None));

        Assert.Equal("backup_falhou", erro.Codigo);
        Assert.Empty(armazenamento.Arquivos);
        var registrado = await db.CrmBackups.AsNoTracking().SingleAsync();
        Assert.Equal(StatusBackup.Falhou, registrado.Status);
        Assert.Contains("conexão recusada", registrado.Erro);

        await Assert.ThrowsAsync<CrmBusinessException>(() => Service(db, armazenamento).AbrirAsync(registrado.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GuardaSoOsMaisRecentes_EApagaOsArquivosAntigos()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        var armazenamento = new ArmazenamentoFalso();
        // 30 backups antigos já guardados.
        for (var i = 0; i < BackupService.QuantidadeGuardada; i++)
        {
            var chave = $"backups/antigo-{i:00}.dump";
            armazenamento.Arquivos[chave] = [1];
            db.CrmBackups.Add(new CrmBackup
            {
                Status = StatusBackup.Concluido, Origem = OrigemBackup.Automatico, Chave = chave,
                CriadoEm = DateTimeOffset.UtcNow.AddDays(-(i + 1)),
            });
        }
        await db.SaveChangesAsync();

        await Service(db, armazenamento).GerarAsync(OrigemBackup.Manual, CancellationToken.None);

        db.ChangeTracker.Clear();
        Assert.Equal(BackupService.QuantidadeGuardada, await db.CrmBackups.CountAsync(b => b.Status == StatusBackup.Concluido));
        // O mais antigo (30 dias) saiu do armazenamento e da lista.
        Assert.DoesNotContain("backups/antigo-29.dump", armazenamento.Arquivos.Keys);
        Assert.Equal(StatusBackup.Removido, (await db.CrmBackups.SingleAsync(b => b.Chave == "backups/antigo-29.dump")).Status);
        Assert.Equal(BackupService.QuantidadeGuardada, (await Service(db, armazenamento).ListarAsync(CancellationToken.None)).Count);
    }

    [Theory]
    [InlineData("2026-09-30T05:00:00Z", "2026-09-30T06:00:00Z")] // 02:00 em Brasília → 03:00 de hoje
    [InlineData("2026-09-30T06:30:00Z", "2026-10-01T06:00:00Z")] // 03:30 em Brasília → amanhã
    public void ProximoBackup_EAs3DaManhaEmBrasilia(string agora, string esperado) =>
        Assert.Equal(DateTimeOffset.Parse(esperado), BackupDiarioBackgroundService.ProximoHorario(DateTimeOffset.Parse(agora)));
}
