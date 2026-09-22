using System.Reflection;
using CssVision.Web.Authorization;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Storage;
using Microsoft.AspNetCore.Identity;

namespace CssVision.Web.Data.Seed;

/// <summary>
/// Cria as contas de login dos consultores comerciais reais (papel Comercial), a partir da
/// planilha de consultores fornecida. Idempotente por e-mail — seguro rodar toda inicialização.
/// Senha inicial única para todos; cada consultor deve trocá-la no próprio perfil.
/// </summary>
public static class ConsultorSeeder
{
    private const string SenhaInicial = "Senha@123";
    private const string PastaFotos = "consultores";

    private static readonly (string Nome, string Email, string Telefone, string? FotoArquivo)[] Consultores =
    [
        ("Naiara Silva", "naiara-silva1306@outlook.com", "31 7366-5233", "naiara-silva.jpeg"),
        ("Leticia Martins", "lleticiamartinsp@gmail.com", "31 7366-5233", "leticia-martins.jpeg"),
        ("Roberta Sobrinho", "robertasobrinho28@gmail.com", "31 9790-7704", "roberta-sobrinho.jpeg"),
        ("Maria Gabriela", "mvpconsultoriaprotecao@gmail.com", "31 7211-7988", "maria-gabriela.jpeg"),
        ("Adriele Rodrigues", "gscvendas05@gmail.com", "31 9314-5435", "adriele-rodrigues.jpeg"),
        ("Hanalise", "vendasapvs10@gmail.com", "31 9362-8139", "hanalise.jpeg"),
        ("Jefferson Moura", "jeffersonmoura1221@gmail.com", "31 8460-9880", "jefferson-moura.jpeg"),
        ("Talita Martorelli", "martorellitalita@gmail.com", "31 9986-8504", "talita-martorelli.jpeg"),
        ("Carol Barcelos", "carolbarcelosagv@gmail.com", "31 9135-2621", "carol-barcelos.jpeg"),
        ("Isabel Oliveira", "isabelcrg19@gmail.com", "31 8353-9792", "isabel-oliveira.jpeg"),
        ("Leticia Magalhães", "leticiapaulamagalhaes896@gmail.com", "31 7213-4874", "leticia-magalhaes.jpeg"),
        ("Israella Luiza", "israellaluiza90@gmail.com", "31 7180-0444", "israella-luiza.jpeg"),
        ("João", "joaohenriquecontato@hotmail.com", "31 7348-5441", "joao.jpeg"),
        ("Thayane", "micelle123@hotmail.com", "31 9434-2460", "thayane.jpeg"),
        ("Laura Diniz", "lauradiniz09az@gmail.com", "31 7594-8003", "laura-diniz.png"),
        ("Lucas Benevenuto", "benevenuto.uagv@outlook.com", "31 99207-6873", "lucas-benevenuto.jpeg"),
        ("Luelle Souza", "gsccvendas07@gmail.com", "31 7335-3345", "luelle-souza.jpeg"),

        // "Dionathan"/"Jhonatan" fica de fora por decisão do Henrique — sem e-mail na planilha
        // e o Identity exige e-mail único por conta.
    ];

    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var storage = services.GetRequiredService<IFileStorageService>();

        foreach (var (nome, email, telefone, fotoArquivo) in Consultores)
        {
            var usuario = await userManager.FindByEmailAsync(email);
            if (usuario is null)
            {
                usuario = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    EmailConfirmed = true,
                    NomeCompleto = nome,
                    PhoneNumber = DocumentValidation.NormalizarTelefone(telefone),
                };

                var resultado = await userManager.CreateAsync(usuario, SenhaInicial);
                if (!resultado.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Falha ao criar consultor {nome} ({email}): {string.Join("; ", resultado.Errors.Select(e => e.Description))}");
                }

                await userManager.AddToRoleAsync(usuario, Roles.Comercial);
            }

            // Reprocessa quem ainda não tem foto válida no storage atual (contas criadas antes da
            // foto virar upload real, ou cujo FotoUrl sobrou do esquema antigo de caminho fixo em
            // wwwroot) — sem isso, uma troca de storage (disco -> S3) deixaria fotos já semeadas
            // permanentemente quebradas, já que o resto do laço é pulado pra quem já existe.
            if (fotoArquivo is not null && PrecisaSubirFoto(usuario.FotoUrl))
            {
                usuario.FotoUrl = await SubirFotoAsync(storage, fotoArquivo, ct);
                await userManager.UpdateAsync(usuario);
            }
        }
    }

    private static bool PrecisaSubirFoto(string? fotoUrlAtual) =>
        string.IsNullOrEmpty(fotoUrlAtual) || fotoUrlAtual.StartsWith("/uploads/", StringComparison.Ordinal);

    private static async Task<string> SubirFotoAsync(IFileStorageService storage, string nomeArquivo, CancellationToken ct)
    {
        var recurso = $"{typeof(ConsultorSeeder).Namespace}.ConsultorPhotos.{nomeArquivo}";
        await using var conteudo = Assembly.GetExecutingAssembly().GetManifestResourceStream(recurso)
            ?? throw new InvalidOperationException($"Foto embutida não encontrada: {recurso}");

        var contentType = Path.GetExtension(nomeArquivo).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            _ => "image/jpeg",
        };

        return await storage.SalvarAsync(PastaFotos, nomeArquivo, conteudo, contentType, ct);
    }
}
