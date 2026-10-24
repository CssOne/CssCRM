using System.Security.Cryptography;

namespace CssVision.Web.Services.Crm;

/// <summary>Senhas que o sistema cria por conta própria (contas de consultores e de vendedores importados).</summary>
public static class SenhaSegura
{
    /// <summary>
    /// A senha inicial que todas as contas de consultor tinham antes (estava escrita no código). Existe aqui só para o login
    /// reconhecê-la e obrigar a troca — nenhuma conta nova nasce mais com ela.
    /// </summary>
    public const string SenhaInicialLegada = "Senha@123";

    private const string Minusculas = "abcdefghijkmnopqrstuvwxyz";
    private const string Maiusculas = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Digitos = "23456789";
    private const string Simbolos = "!@#$%&*?";

    /// <summary>Senha aleatória de 24 caracteres (maiúscula, minúscula, dígito e símbolo), que ninguém conhece: o dono cria a dele por "Esqueci minha senha".</summary>
    public static string Gerar()
    {
        var todos = Minusculas + Maiusculas + Digitos + Simbolos;
        var caracteres = new List<char>
        {
            Sortear(Minusculas), Sortear(Maiusculas), Sortear(Digitos), Sortear(Simbolos),
        };
        while (caracteres.Count < 24) caracteres.Add(Sortear(todos));

        // Embaralha para os quatro obrigatórios não ficarem sempre no começo.
        for (var i = caracteres.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (caracteres[i], caracteres[j]) = (caracteres[j], caracteres[i]);
        }
        return new string(caracteres.ToArray());
    }

    private static char Sortear(string alfabeto) => alfabeto[RandomNumberGenerator.GetInt32(alfabeto.Length)];
}
