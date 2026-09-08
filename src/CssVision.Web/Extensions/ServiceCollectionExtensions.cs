using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Marketing;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCrmDataAccess(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Default"),
                npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName)));

        return services;
    }

    public static IServiceCollection AddCrmIdentity(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
            {
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.User.RequireUniqueEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "CssVision.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = true;
            options.LoginPath = "/api/account/login";
            options.LogoutPath = "/api/account/logout";

            // API: responder 401/403 em JSON em vez de redirecionar para uma página de login HTML.
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        return services;
    }

    public static IServiceCollection AddCrmAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyNames.AreaAdministrativa, p => p.RequireRole(Roles.Administrativos))
            .AddPolicy(PolicyNames.AreaComercial, p => p.RequireRole(Roles.Comerciais))
            .AddPolicy(PolicyNames.GestaoComercial, p => p.RequireRole(Roles.GestaoComercial))
            .AddPolicy(PolicyNames.VisaoTotalComercial, p => p.RequireRole(Roles.VisaoTotal));

        return services;
    }

    public static IServiceCollection AddCrmServices(this IServiceCollection services)
    {
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IEquipeComercialService, EquipeComercialService>();
        services.AddScoped<IAuditSink, CrmAuditLogSink>();
        services.AddScoped<ILeadService, LeadService>();
        services.AddScoped<ILeadKanbanService, LeadKanbanService>();
        services.AddScoped<IOpportunityService, OpportunityService>();
        services.AddScoped<IPipelineService, PipelineService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IGoalService, GoalService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IManagementService, ManagementService>();

        return services;
    }

    public static IServiceCollection AddMetaLeadAdsIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MetaLeadAdsOptions>(configuration.GetSection(MetaLeadAdsOptions.SectionName));
        services.AddHttpClient<IMetaGraphClient, MetaGraphClient>();
        services.AddScoped<MetaLeadIngestionService>();

        return services;
    }
}
