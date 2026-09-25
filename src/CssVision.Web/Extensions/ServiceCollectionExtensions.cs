using Amazon.S3;
using CssVision.Web.Authorization;
using CssVision.Web.Data;
using CssVision.Web.Domain.Identity;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Email;
using CssVision.Web.Services.Marketing;
using CssVision.Web.Services.Notion;
using CssVision.Web.Services.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
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

        // Chaves que assinam o cookie de login guardadas no banco: sem isso cada deploy (container
        // novo) gerava chaves novas e derrubava o login de todo mundo.
        services.AddDataProtection()
            .SetApplicationName("CssVision.Web")
            .PersistKeysToDbContext<ApplicationDbContext>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "CssVision.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            // Sem deslogar sozinho durante o uso: 30 dias, renovados a cada acesso (sliding) — só sai
            // quem clicar em "Sair" ou ficar 30 dias sem abrir o CRM.
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
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

    public static IServiceCollection AddPublicLeadIntakeCors(this IServiceCollection services)
    {
        services.AddCors(options => options.AddPolicy(CorsPolicies.PublicLeadIntake, policy =>
            policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

        return services;
    }

    public static IServiceCollection AddCrmAuthorizationPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddPolicy(PolicyNames.AreaAdministrativa, p => p.RequireRole(Roles.Administrativos))
            .AddPolicy(PolicyNames.AreaComercial, p => p.RequireRole(Roles.Comerciais))
            .AddPolicy(PolicyNames.GestaoComercial, p => p.RequireRole(Roles.GestaoComercial))
            .AddPolicy(PolicyNames.VisaoTotalComercial, p => p.RequireRole(Roles.VisaoTotal))
            .AddPolicy(PolicyNames.AreaMarketing, p => p.RequireRole(Roles.AreaMarketing));

        return services;
    }

    public static IServiceCollection AddCrmServices(this IServiceCollection services)
    {
        services.AddSingleton<ICrmEventHub, CrmEventHub>();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<IEquipeComercialService, EquipeComercialService>();
        services.AddScoped<IAuditSink, CrmAuditLogSink>();
        services.AddScoped<ILeadService, LeadService>();
        services.AddScoped<ILeadKanbanService, LeadKanbanService>();
        services.AddScoped<ILeadAssignmentService, LeadAssignmentService>();
        services.AddHostedService<DistribuicaoLeadsBackgroundService>();
        services.AddScoped<IOpportunityService, OpportunityService>();
        services.AddScoped<IPipelineService, PipelineService>();
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IGoalService, GoalService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<IManagementService, ManagementService>();
        services.AddScoped<IPublicLeadIntakeService, PublicLeadIntakeService>();
        services.AddScoped<IUserManagementService, UserManagementService>();
        services.AddScoped<IAnnouncementService, AnnouncementService>();
        services.AddScoped<IMarketingService, MarketingService>();

        return services;
    }

    public static IServiceCollection AddCrmEmailSender(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddScoped<IEmailSender, SmtpEmailSender>();

        return services;
    }

    /// <summary>
    /// Usa S3 quando Storage:S3:BucketName está configurado (produção com IAM role na instância —
    /// nenhuma credencial em config) — caso contrário, disco local (dev, ou produção simples de
    /// instância única). Ver LocalFileStorageService / S3FileStorageService.
    /// </summary>
    public static IServiceCollection AddCrmFileStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var bucketName = configuration[$"{S3StorageOptions.SectionName}:BucketName"];
        if (string.IsNullOrWhiteSpace(bucketName))
        {
            services.AddScoped<IFileStorageService, LocalFileStorageService>();
            return services;
        }

        services.Configure<S3StorageOptions>(configuration.GetSection(S3StorageOptions.SectionName));
        services.AddSingleton<IAmazonS3>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<S3StorageOptions>>().Value;
            return new AmazonS3Client(Amazon.RegionEndpoint.GetBySystemName(options.Region));
        });
        services.AddScoped<IFileStorageService, S3FileStorageService>();

        return services;
    }

    public static IServiceCollection AddMetaLeadAdsIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<MetaLeadAdsOptions>(configuration.GetSection(MetaLeadAdsOptions.SectionName));
        services.AddHttpClient<IMetaGraphClient, MetaGraphClient>();
        services.AddScoped<MetaLeadIngestionService>();

        services.Configure<MetaCapiOptions>(configuration.GetSection(MetaCapiOptions.SectionName));
        services.AddHttpClient<IMetaConversionService, MetaConversionService>();

        return services;
    }

    /// <summary>
    /// Sincronização periódica com o Notion — só liga o serviço em segundo plano se um token
    /// estiver configurado (NotionSync:Token, normalmente via Secrets Manager em produção).
    /// </summary>
    public static IServiceCollection AddNotionSync(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<NotionSyncOptions>(configuration.GetSection("NotionSync"));
        services.AddScoped<NotionSyncService>();

        var enabled = !string.IsNullOrWhiteSpace(configuration["NotionSync:Token"]);
        services.PostConfigure<NotionSyncOptions>(o => o.Enabled = enabled);
        if (enabled)
        {
            services.AddHostedService<NotionSyncBackgroundService>();
        }

        return services;
    }
}
