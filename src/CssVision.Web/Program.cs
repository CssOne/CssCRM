using CssVision.Web.Api;
using CssVision.Web.Data;
using CssVision.Web.Data.Seed;
using CssVision.Web.Extensions;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    // Nunca logar corpo de requisição/resposta por padrão: pode conter CPF/CNPJ, telefone, e-mail.
    .WriteTo.Console());

builder.Services.AddCrmDataAccess(builder.Configuration);
builder.Services.AddCrmIdentity();
builder.Services.AddCrmAuthorizationPolicies();
builder.Services.AddCrmServices();

builder.Services.AddControllers();
builder.Services.AddExceptionHandler<CrmExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSpaStaticFiles(configuration => { configuration.RootPath = "ClientApp/dist"; });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedAsync(scope.ServiceProvider);
    await CrmSeeder.SeedAsync(db);
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseExceptionHandler();
app.UseHttpsRedirection();
app.UseSerilogRequestLogging();

app.UseStaticFiles();
if (!app.Environment.IsDevelopment())
{
    app.UseSpaStaticFiles();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Endpoints da API precisam ser despachados aqui, antes do fallback do SPA — caso contrário,
// UseEndpoints (necessário para MapControllers realmente executar as actions) só seria inserido
// implicitamente no fim do pipeline, depois do UseSpa, e rotas sem [Authorize] (ex: login/session)
// acabariam sendo interceptadas pelo proxy do SPA em vez de chegarem aos controllers.
#pragma warning disable ASP0014
app.UseEndpoints(endpoints => endpoints.MapControllers());
#pragma warning restore ASP0014

app.UseSpa(spa =>
{
    spa.Options.SourcePath = "ClientApp";
    if (app.Environment.IsDevelopment())
    {
        spa.UseProxyToSpaDevelopmentServer("http://localhost:5173");
    }
});

app.Run();

public partial class Program;
