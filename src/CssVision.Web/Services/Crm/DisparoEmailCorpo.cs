namespace CssVision.Web.Services.Crm;

/// <summary>Montagem do e-mail do disparo: texto simples do consultor vira HTML seguro, com a assinatura (nome, telefone, WhatsApp) e o link de descadastro.</summary>
public static class DisparoEmailCorpo
{
    /// <summary>Escapa só o que é markup — mantém acentos legíveis no HTML (WebUtility.HtmlEncode viraria "&amp;#225;").</summary>
    public static string Escapar(string texto) => texto.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    public static string PrimeiroNome(string nomeCliente) => nomeCliente.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? nomeCliente;

    /// <summary>{nome} vira o primeiro nome do cliente — vale para o assunto e para a mensagem.</summary>
    public static string SubstituirNome(string texto, string nomeCliente) => texto.Replace("{nome}", PrimeiroNome(nomeCliente), StringComparison.OrdinalIgnoreCase);

    /// <summary>Link que abre a conversa no WhatsApp do consultor (telefone BR com DDD, 10 ou 11 dígitos); nulo se o telefone não serve.</summary>
    public static string? LinkWhatsApp(string? telefone, string nomeConsultor)
    {
        var digitos = DocumentValidation.NormalizarTelefone(telefone);
        if (digitos is not { Length: 10 or 11 }) return null;
        var texto = Uri.EscapeDataString($"Olá {PrimeiroNome(nomeConsultor)}, recebi o seu e-mail e quero falar com você.");
        return $"https://wa.me/55{digitos}?text={texto}";
    }

    public static string Montar(string mensagem, string nomeCliente, string nomeConsultor, string? telefoneConsultor, string emailConsultor, string? urlDescadastro)
    {
        var texto = Escapar(SubstituirNome(mensagem, nomeCliente)).Replace("\r\n", "\n").Replace("\n", "<br>");
        var linhaTelefone = string.IsNullOrWhiteSpace(telefoneConsultor) ? "" : $"<br>Telefone/WhatsApp: {Escapar(telefoneConsultor)}";
        var whatsapp = LinkWhatsApp(telefoneConsultor, nomeConsultor);
        var botaoWhatsApp = whatsapp is null ? "" : $"<p><a href=\"{whatsapp}\" style=\"display:inline-block;background:#25D366;color:#fff;text-decoration:none;font-weight:bold;font-size:14px;padding:10px 18px;border-radius:6px\">Chamar no WhatsApp</a></p>";
        var descadastro = string.IsNullOrEmpty(urlDescadastro)
            ? ""
            : $"<p style=\"font-size:12px;color:#888\">Não quer mais receber e-mails assim? <a href=\"{Escapar(urlDescadastro)}\" style=\"color:#888\">Cancelar inscrição</a>.</p>";
        return $"""
            <div style="font-family:Arial,Helvetica,sans-serif;font-size:15px;line-height:1.5;color:#222">
              <p>{texto}</p>
              <hr style="border:none;border-top:1px solid #ddd;margin:24px 0 12px">
              {botaoWhatsApp}
              <p style="font-size:13px;color:#666"><strong>{Escapar(nomeConsultor)}</strong> · CSS Brasil{linhaTelefone}<br>{Escapar(emailConsultor)}</p>
              {descadastro}
            </div>
            """;
    }
}
