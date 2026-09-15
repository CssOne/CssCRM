using CssVision.Web.Api;
using CssVision.Web.Data;
using CssVision.Web.Data.Seed;
using CssVision.Web.Extensions;
using Microsoft.AspNetCore.HttpOverrides;
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
builder.Services.AddCrmEmailSender(builder.Configuration);
builder.Services.AddCrmFileStorage(builder.Configuration);
builder.Services.AddMetaLeadAdsIntegration(builder.Configuration);
builder.Services.AddPublicLeadIntakeCors();

builder.Services.AddControllers();
builder.Services.AddExceptionHandler<CrmExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSpaStaticFiles(configuration => { configuration.RootPath = "ClientApp/dist"; });

builder.Services.AddHealthChecks()
    .AddNpgSql(builder.Configuration.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default não configurada."));

// Atrás de um load balancer (ALB), a app só enxerga a conexão interna do LB — sem isso, o scheme
// chega sempre como "http" e IP do cliente como o do próprio LB, quebrando UseHttpsRedirection/HSTS
// e qualquer log/regra baseado em IP real. O LB já fica numa subnet controlada, então confiamos nele.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

// Precisa vir antes de qualquer middleware que decida algo com base em scheme/IP (HTTPS redirect,
// HSTS, rate limiting por IP etc.) — senão eles leem os valores da conexão interna com o LB, não os
// do cliente real.
app.UseForwardedHeaders();

// Migrations e seeds essenciais (papéis, etapas do funil, contas reais dos consultores) rodam em
// qualquer ambiente — sem isso o banco sobe vazio em produção e ninguém consegue logar. São
// idempotentes, seguros de rodar toda inicialização.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
    await IdentitySeeder.SeedRolesAsync(scope.ServiceProvider);
    await CrmSeeder.SeedAsync(db);
    await ConsultorSeeder.SeedAsync(scope.ServiceProvider);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    using var scope = app.Services.CreateScope();
    await IdentitySeeder.SeedDemoUsersAsync(scope.ServiceProvider);
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

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// Endpoints da API precisam ser despachados aqui, antes do fallback do SPA — caso contrário,
// UseEndpoints (necessário para MapControllers realmente executar as actions) só seria inserido
// implicitamente no fim do pipeline, depois do UseSpa, e rotas sem [Authorize] (ex: login/session)
// acabariam sendo interceptadas pelo proxy do SPA em vez de chegarem aos controllers.
#pragma warning disable ASP0014
app.UseEndpoints(endpoints =>
{
    endpoints.MapControllers();
    endpoints.MapHealthChecks("/health");
});
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
