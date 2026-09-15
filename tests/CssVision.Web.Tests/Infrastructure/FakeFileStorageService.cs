using CssVision.Web.Services.Storage;

namespace CssVision.Web.Tests.Infrastructure;

/// <summary>Armazenamento falso só para satisfazer o construtor de serviços que recebem IFileStorageService —
/// os testes de unidade não exercitam os caminhos que gravam anexo/foto de verdade.</summary>
public sealed class FakeFileStorageService : IFileStorageService
{
    public Task<string> SalvarAsync(string pasta, string nomeArquivo, Stream conteudo, string? contentType, CancellationToken ct) =>
        Task.FromResult($"/uploads/{pasta}/{nomeArquivo}");

    public Task ExcluirSeExistirAsync(string? url, CancellationToken ct = default) => Task.CompletedTask;
}
