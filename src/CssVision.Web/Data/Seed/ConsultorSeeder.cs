using CssVision.Web.Authorization;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
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

    private static readonly (string Nome, string Email, string Telefone, string? FotoUrl)[] Consultores =
    [
        ("Naiara Silva", "naiara-silva1306@outlook.com", "31 7366-5233", "/uploads/consultores/naiara-silva.jpeg"),
        ("Leticia Martins", "lleticiamartinsp@gmail.com", "31 7366-5233", "/uploads/consultores/leticia-martins.jpeg"),
        ("Roberta Sobrinho", "robertasobrinho28@gmail.com", "31 9790-7704", "/uploads/consultores/roberta-sobrinho.jpeg"),
        ("Maria Gabriela", "mvpconsultoriaprotecao@gmail.com", "31 7211-7988", "/uploads/consultores/maria-gabriela.jpeg"),
        ("Adriele Rodrigues", "gscvendas05@gmail.com", "31 9314-5435", "/uploads/consultores/adriele-rodrigues.jpeg"),
        ("Hanalise", "vendasapvs10@gmail.com", "31 9362-8139", "/uploads/consultores/hanalise.jpeg"),
        ("Jefferson Moura", "jeffersonmoura1221@gmail.com", "31 8460-9880", "/uploads/consultores/jefferson-moura.jpeg"),
        ("Talita Martorelli", "martorellitalita@gmail.com", "31 9986-8504", "/uploads/consultores/talita-martorelli.jpeg"),
        ("Carol Barcelos", "carolbarcelosagv@gmail.com", "31 9135-2621", "/uploads/consultores/carol-barcelos.jpeg"),
        ("Isabel Oliveira", "isabelcrg19@gmail.com", "31 8353-9792", "/uploads/consultores/isabel-oliveira.jpeg"),
        ("Leticia Magalhães", "leticiapaulamagalhaes896@gmail.com", "31 7213-4874", "/uploads/consultores/leticia-magalhaes.jpeg"),
        ("Israella Luiza", "israellaluiza90@gmail.com", "31 7180-0444", "/uploads/consultores/israella-luiza.jpeg"),
        ("João", "joaohenriquecontato@hotmail.com", "31 7348-5441", "/uploads/consultores/joao.jpeg"),
        ("Thayane", "micelle123@hotmail.com", "31 9434-2460", "/uploads/consultores/thayane.jpeg"),
        ("Laura Diniz", "lauradiniz09az@gmail.com", "31 7594-8003", "/uploads/consultores/laura-diniz.png"),
        ("Lucas Benevenuto", "benevenuto.uagv@outlook.com", "31 99207-6873", "/uploads/consultores/lucas-benevenuto.jpeg"),
        // Foto ainda não recebida como arquivo — só a imagem colada no chat, sem bytes acessíveis pra salvar.
        ("Luelle Souza", "gsccvendas07@gmail.com", "31 7335-3345", null),

        // "Dionathan"/"Jhonatan" fica de fora por decisão do Henrique — sem e-mail na planilha
        // e o Identity exige e-mail único por conta.
    ];

    public static async Task SeedAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

        foreach (var (nome, email, telefone, fotoUrl) in Consultores)
        {
            var usuario = await userManager.FindByEmailAsync(email);
            if (usuario is not null) continue;

            usuario = new ApplicationUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                NomeCompleto = nome,
                PhoneNumber = DocumentValidation.NormalizarTelefone(telefone),
                FotoUrl = fotoUrl,
            };

            var resultado = await userManager.CreateAsync(usuario, SenhaInicial);
            if (!resultado.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Falha ao criar consultor {nome} ({email}): {string.Join("; ", resultado.Errors.Select(e => e.Description))}");
            }

            await userManager.AddToRoleAsync(usuario, Roles.Comercial);
        }
    }
}
