using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var connectionString = Environment.GetEnvironmentVariable("ADMIN_SEEDER_CONNECTION_STRING")
    ?? throw new InvalidOperationException("Defina ADMIN_SEEDER_CONNECTION_STRING antes de rodar.");
var email = Environment.GetEnvironmentVariable("ADMIN_SEEDER_EMAIL")
    ?? throw new InvalidOperationException("Defina ADMIN_SEEDER_EMAIL antes de rodar.");
var senha = Environment.GetEnvironmentVariable("ADMIN_SEEDER_SENHA")
    ?? throw new InvalidOperationException("Defina ADMIN_SEEDER_SENHA antes de rodar.");
var nomeCompleto = Environment.GetEnvironmentVariable("ADMIN_SEEDER_NOME") ?? "Administrador";

var services = new ServiceCollection();
services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
services.AddSingleton<ICurrentUserService, AdminSeederCurrentUser>();
services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(connectionString, npg => npg.EnableRetryOnFailure(5)));
services.AddIdentity<ApplicationUser, ApplicationRole>(o =>
    {
        o.Password.RequiredLength = 8;
        o.Password.RequireNonAlphanumeric = false;
        o.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

await using var provider = services.BuildServiceProvider();
using var scope = provider.CreateScope();
var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();

if (!await roleManager.RoleExistsAsync(Roles.Admin))
{
    throw new InvalidOperationException($"Papel '{Roles.Admin}' não existe no banco — rode as migrations/seed do app antes.");
}

var existente = await userManager.FindByEmailAsync(email);
if (existente is not null)
{
    Console.WriteLine($"Usuário '{email}' já existe (Id={existente.Id}). Nada a fazer.");
    if (!await userManager.IsInRoleAsync(existente, Roles.Admin))
    {
        await userManager.AddToRoleAsync(existente, Roles.Admin);
        Console.WriteLine("Papel Admin adicionado ao usuário existente.");
    }
    return;
}

var usuario = new ApplicationUser
{
    UserName = email,
    Email = email,
    EmailConfirmed = true,
    NomeCompleto = nomeCompleto,
};

var resultado = await userManager.CreateAsync(usuario, senha);
if (!resultado.Succeeded)
{
    throw new InvalidOperationException(string.Join("; ", resultado.Errors.Select(e => e.Description)));
}

await userManager.AddToRoleAsync(usuario, Roles.Admin);
Console.WriteLine($"Usuário admin '{email}' criado com sucesso (Id={usuario.Id}).");

sealed class AdminSeederCurrentUser : ICurrentUserService
{
    public Guid UserId => Guid.Empty;
    public string? NomeCompleto => "Admin Seeder";
    public bool IsAuthenticated => true;
    public bool IsInRole(string role) => false;
    public bool TemVisaoTotal => true;
    public bool PodeGerirComercial => true;
    public bool IsGestorComercial => false;
}
