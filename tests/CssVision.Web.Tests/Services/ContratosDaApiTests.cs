using System.ComponentModel.DataAnnotations;
using System.Reflection;
using CssVision.Web.Api.Contracts.Crm;
using Xunit;

namespace CssVision.Web.Tests.Services;

public class ContratosDaApiTests
{
    /// <summary>
    /// Em record posicional, validação na propriedade (<c>[property: Required]</c>) faz o ASP.NET devolver 500 em toda chamada
    /// ("validation metadata ... will be ignored"): o aviso de pagamento e o Suporte nunca chegaram a ser enviados por causa disso.
    /// O atributo tem de ficar no parâmetro do construtor.
    /// </summary>
    [Fact]
    public void RecordsDaApi_NaoTemValidacaoNaPropriedadeDoConstrutor()
    {
        var erros = new List<string>();
        foreach (var tipo in typeof(AvisoPagamentoCreateRequest).Assembly.GetTypes().Where(t => t.Namespace?.StartsWith("CssVision.Web.Api.Contracts") == true))
        {
            if (tipo.GetMethod("<Clone>$") is null) continue; // não é record
            var parametros = tipo.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.Name).ToHashSet();
            foreach (var propriedade in tipo.GetProperties())
            {
                if (parametros.Contains(propriedade.Name) && propriedade.GetCustomAttributes<ValidationAttribute>(true).Any())
                    erros.Add($"{tipo.Name}.{propriedade.Name}");
            }
        }
        Assert.True(erros.Count == 0, "Validação na propriedade de record (use o atributo direto no parâmetro): " + string.Join(", ", erros));
    }
}
