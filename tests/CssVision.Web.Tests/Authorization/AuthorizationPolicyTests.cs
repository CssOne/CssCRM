using System.Security.Claims;
using CssVision.Web.Authorization;
using CssVision.Web.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CssVision.Web.Tests.Authorization;

/// <summary>
/// Testa as policies de autorização usadas pelos controllers (ver ServiceCollectionExtensions.
/// AddCrmAuthorizationPolicies e os atributos [Authorize] nos controllers). Validar no nível de
/// policy é equivalente a validar as rotas em si — é o que o ASP.NET Core avalia antes de
/// despachar qualquer action — sem depender de um banco de dados real para subir a aplicação
/// inteira em um WebApplicationFactory.
/// </summary>
public class AuthorizationPolicyTests
{
    private static IAuthorizationService CriarAuthorizationService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCrmAuthorizationPolicies();
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static ClaimsPrincipal Usuario(params string[] papeis)
    {
        var identity = new ClaimsIdentity(papeis.Select(p => new Claim(ClaimTypes.Role, p)), "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static readonly ClaimsPrincipal Anonimo = new(new ClaimsIdentity());

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.GestorMaster, true)]
    [InlineData(Roles.GestorComercial, false)]
    [InlineData(Roles.Comercial, false)]
    public async Task AreaAdministrativa_ApenasAdminEGestorMaster(string papel, bool esperado)
    {
        var auth = CriarAuthorizationService();
        var resultado = await auth.AuthorizeAsync(Usuario(papel), PolicyNames.AreaAdministrativa);
        Assert.Equal(esperado, resultado.Succeeded);
    }

    [Fact]
    public async Task AreaAdministrativa_DeveNegar_UsuarioAnonimo()
    {
        var auth = CriarAuthorizationService();
        var resultado = await auth.AuthorizeAsync(Anonimo, PolicyNames.AreaAdministrativa);
        Assert.False(resultado.Succeeded);
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.GestorMaster, true)]
    [InlineData(Roles.GestorComercial, true)]
    [InlineData(Roles.Comercial, true)]
    public async Task AreaComercial_TodosOsPapeisComerciaisTemAcesso(string papel, bool esperado)
    {
        var auth = CriarAuthorizationService();
        var resultado = await auth.AuthorizeAsync(Usuario(papel), PolicyNames.AreaComercial);
        Assert.Equal(esperado, resultado.Succeeded);
    }

    [Fact]
    public async Task AreaComercial_DeveNegar_UsuarioSemPapel()
    {
        var auth = CriarAuthorizationService();
        var resultado = await auth.AuthorizeAsync(Usuario("PapelInexistente"), PolicyNames.AreaComercial);
        Assert.False(resultado.Succeeded);
    }

    [Fact]
    public async Task AreaComercial_DeveNegar_UsuarioAnonimo()
    {
        var auth = CriarAuthorizationService();
        var resultado = await auth.AuthorizeAsync(Anonimo, PolicyNames.AreaComercial);
        Assert.False(resultado.Succeeded);
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.GestorMaster, true)]
    [InlineData(Roles.GestorComercial, true)]
    [InlineData(Roles.Comercial, false)]
    public async Task GestaoComercial_ComercialNaoPodeGerir(string papel, bool esperado)
    {
        var auth = CriarAuthorizationService();
        var resultado = await auth.AuthorizeAsync(Usuario(papel), PolicyNames.GestaoComercial);
        Assert.Equal(esperado, resultado.Succeeded);
    }

    [Theory]
    [InlineData(Roles.Admin, true)]
    [InlineData(Roles.GestorMaster, true)]
    [InlineData(Roles.GestorComercial, false)]
    [InlineData(Roles.Comercial, false)]
    public async Task VisaoTotalComercial_ApenasAdminEGestorMaster(string papel, bool esperado)
    {
        var auth = CriarAuthorizationService();
        var resultado = await auth.AuthorizeAsync(Usuario(papel), PolicyNames.VisaoTotalComercial);
        Assert.Equal(esperado, resultado.Succeeded);
    }
}
