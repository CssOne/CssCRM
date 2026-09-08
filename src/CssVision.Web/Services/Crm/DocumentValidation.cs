using System.Text.RegularExpressions;

namespace CssVision.Web.Services.Crm;

/// <summary>Normalização e validação de CPF, CNPJ, telefone e e-mail (padrão brasileiro).</summary>
public static partial class DocumentValidation
{
    [GeneratedRegex(@"\D")]
    private static partial Regex NaoDigitoRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    public static string SomenteDigitos(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? string.Empty : NaoDigitoRegex().Replace(valor, string.Empty);

    /// <summary>Normaliza CPF (11 dígitos) ou CNPJ (14 dígitos). Retorna null se inválido.</summary>
    public static string? NormalizarDocumento(string? documento, out bool valido)
    {
        valido = false;
        var digitos = SomenteDigitos(documento);
        if (digitos.Length == 0) return null;

        if (digitos.Length == 11)
        {
            valido = ValidarCpf(digitos);
            return digitos;
        }

        if (digitos.Length == 14)
        {
            valido = ValidarCnpj(digitos);
            return digitos;
        }

        return digitos;
    }

    public static string? NormalizarTelefone(string? telefone)
    {
        var digitos = SomenteDigitos(telefone);
        if (digitos.Length == 0) return null;

        // Remove código do país 55 quando presente com DDD+número completo (12/13 dígitos totais).
        if (digitos.Length is 12 or 13 && digitos.StartsWith("55"))
        {
            digitos = digitos[2..];
        }

        return digitos;
    }

    public static string? NormalizarEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;
        var normalizado = email.Trim().ToLowerInvariant();
        return EmailRegex().IsMatch(normalizado) ? normalizado : null;
    }

    public static bool EmailValido(string? email) => !string.IsNullOrWhiteSpace(email) && EmailRegex().IsMatch(email.Trim());

    public static string FormatarDocumento(string? documentoNormalizado)
    {
        if (string.IsNullOrEmpty(documentoNormalizado)) return string.Empty;
        return documentoNormalizado.Length switch
        {
            11 => $"{documentoNormalizado[..3]}.{documentoNormalizado[3..6]}.{documentoNormalizado[6..9]}-{documentoNormalizado[9..]}",
            14 => $"{documentoNormalizado[..2]}.{documentoNormalizado[2..5]}.{documentoNormalizado[5..8]}/{documentoNormalizado[8..12]}-{documentoNormalizado[12..]}",
            _ => documentoNormalizado
        };
    }

    /// <summary>Mascara o documento para uso em listagens (ex: 123.***.**9-00), evitando expor o dado completo.</summary>
    public static string MascararDocumento(string? documentoNormalizado)
    {
        if (string.IsNullOrEmpty(documentoNormalizado)) return string.Empty;
        var formatado = FormatarDocumento(documentoNormalizado);
        return documentoNormalizado.Length == 11
            ? $"{formatado[..3]}.***.**{formatado[^5..]}"
            : $"{formatado[..2]}.***.**{formatado[^9..]}";
    }

    public static bool ValidarCpf(string cpf)
    {
        if (cpf.Length != 11 || cpf.Distinct().Count() == 1) return false;

        int[] multiplicadores1 = [10, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] multiplicadores2 = [11, 10, 9, 8, 7, 6, 5, 4, 3, 2];

        var soma = 0;
        for (var i = 0; i < 9; i++) soma += (cpf[i] - '0') * multiplicadores1[i];
        var resto = soma % 11;
        var digito1 = resto < 2 ? 0 : 11 - resto;
        if (digito1 != cpf[9] - '0') return false;

        soma = 0;
        for (var i = 0; i < 10; i++) soma += (cpf[i] - '0') * multiplicadores2[i];
        resto = soma % 11;
        var digito2 = resto < 2 ? 0 : 11 - resto;
        return digito2 == cpf[10] - '0';
    }

    public static bool ValidarCnpj(string cnpj)
    {
        if (cnpj.Length != 14 || cnpj.Distinct().Count() == 1) return false;

        int[] multiplicadores1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] multiplicadores2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        var soma = 0;
        for (var i = 0; i < 12; i++) soma += (cnpj[i] - '0') * multiplicadores1[i];
        var resto = soma % 11;
        var digito1 = resto < 2 ? 0 : 11 - resto;
        if (digito1 != cnpj[12] - '0') return false;

        soma = 0;
        for (var i = 0; i < 13; i++) soma += (cnpj[i] - '0') * multiplicadores2[i];
        resto = soma % 11;
        var digito2 = resto < 2 ? 0 : 11 - resto;
        return digito2 == cnpj[13] - '0';
    }
}
