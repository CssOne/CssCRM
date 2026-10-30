using System.Text;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// E-mail enviado na hora em que o lead chega: agradece, explica como funciona a Universo AGV e apresenta o
/// consultor que está atendendo (o mesmo da página de obrigado, pelo rodízio), com foto e botão de WhatsApp.
/// </summary>
public static class LeadBoasVindasEmail
{
    public const string Assunto = "Recebemos sua solicitação de cotação | Universo AGV";

    private static readonly string[] Beneficios =
    [
        "Proteção contra roubo e furto.",
        "Proteção para colisão e perda total.",
        "Danos a terceiros.",
        "Assistência 24 horas.",
        "Carro reserva.",
        "E muito mais.",
    ];

    /// <summary>"silas Dias." → "Silas": primeiro nome, sem pontuação sobrando, só a inicial maiúscula.</summary>
    public static string PrimeiroNome(string? nomeCompleto)
    {
        var primeiro = (nomeCompleto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim('.', ',', ';', '-') ?? "";
        if (primeiro.Length == 0) return "";
        return char.ToUpperInvariant(primeiro[0]) + primeiro[1..].ToLowerInvariant();
    }

    /// <summary>Abre a conversa no WhatsApp do consultor (telefone BR com DDD, 10 ou 11 dígitos); nulo se o telefone não serve.</summary>
    public static string? LinkWhatsApp(string? telefoneConsultor, string nomeConsultor)
    {
        var digitos = DocumentValidation.NormalizarTelefone(telefoneConsultor);
        if (digitos is not { Length: 10 or 11 }) return null;
        var texto = Uri.EscapeDataString($"Olá {PrimeiroNome(nomeConsultor)}, recebi o e-mail da Universo AGV e quero fazer minha cotação.");
        return $"https://wa.me/55{digitos}?text={texto}";
    }

    /// <param name="fotoUrl">Endereço completo (https://...) da foto do consultor; sem foto, aparece a inicial do nome.</param>
    /// <param name="linkWhatsApp">Sem ele o botão "Fazer cotação" não aparece.</param>
    public static string Montar(string nomeCliente, string nomeConsultor, string? fotoUrl, string? linkWhatsApp, string? urlDescadastro)
    {
        var e = DisparoEmailCorpo.Escapar;
        var nome = PrimeiroNome(nomeCliente);
        var saudacao = nome.Length == 0 ? "Olá! Tudo bem?" : $"Olá, {e(nome)}! Tudo bem?";
        var primeiroConsultor = PrimeiroNome(nomeConsultor);

        var beneficios = new StringBuilder();
        foreach (var b in Beneficios) beneficios.Append($"<li style=\"margin:0 0 6px\">{e(b)}</li>");

        var avatar = string.IsNullOrWhiteSpace(fotoUrl)
            ? $"<div style=\"width:88px;height:88px;border-radius:44px;background:#0b3d91;color:#ffffff;font-size:36px;font-weight:bold;line-height:88px;text-align:center\">{e(primeiroConsultor.Length > 0 ? primeiroConsultor[..1] : "A")}</div>"
            : $"<img src=\"{e(fotoUrl)}\" width=\"88\" height=\"88\" alt=\"{e(nomeConsultor)}\" style=\"display:block;width:88px;height:88px;border-radius:44px;object-fit:cover;border:0\">";

        var botao = linkWhatsApp is null
            ? ""
            : $"<tr><td style=\"padding:18px 0 4px\"><a href=\"{e(linkWhatsApp)}\" style=\"display:inline-block;background:#25D366;color:#ffffff;text-decoration:none;font-weight:bold;font-size:16px;padding:14px 32px;border-radius:8px\">Fazer cotação</a></td></tr>";

        var descadastro = string.IsNullOrEmpty(urlDescadastro)
            ? ""
            : $"<p style=\"margin:10px 0 0;font-size:12px;color:#888888\">Não quer mais receber e-mails da Universo AGV? <a href=\"{e(urlDescadastro)}\" style=\"color:#888888\">Cancelar inscrição</a>.</p>";

        return $"""
            <!DOCTYPE html>
            <html lang="pt-BR"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
            <body style="margin:0;padding:0;background:#f3f5f9">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f3f5f9"><tr><td align="center" style="padding:24px 12px">
              <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#ffffff;border-radius:10px;overflow:hidden;font-family:Arial,Helvetica,sans-serif;color:#222222">
                <tr><td style="background:#0b3d91;padding:20px 28px;color:#ffffff;font-size:20px;font-weight:bold">Universo AGV</td></tr>
                <tr><td style="padding:28px 28px 8px;font-size:15px;line-height:1.6">
                  <p style="margin:0 0 14px;font-size:18px"><strong>{saudacao}</strong></p>
                  <p style="margin:0 0 14px">Recebemos sua solicitação de cotação e queremos agradecer pelo seu interesse na Universo AGV.</p>
                  <p style="margin:0 0 14px">Seu próximo passo é simples: nossa equipe comercial vai analisar as informações enviadas e entrar em contato para apresentar as condições disponíveis para o seu veículo.</p>
                  <p style="margin:0 0 6px">Enquanto isso, queremos que você conheça um pouco mais sobre a AGV.</p>
                </td></tr>
                <tr><td style="padding:12px 28px 8px;font-size:15px;line-height:1.6">
                  <p style="margin:0 0 8px;font-size:17px;color:#0b3d91"><strong>Por que conhecer a Universo AGV?</strong></p>
                  <p style="margin:0 0 10px">Com mais de 10 anos de atuação e mais de 1 milhão de veículos já protegidos, a AGV trabalha para oferecer proteção patrimonial e assistência aos seus associados.</p>
                  <p style="margin:0 0 6px">Entre os benefícios disponíveis estão:</p>
                  <ul style="margin:0 0 6px;padding-left:22px">{beneficios}</ul>
                </td></tr>
                <tr><td style="padding:12px 28px 8px;font-size:15px;line-height:1.6">
                  <p style="margin:0 0 8px;font-size:17px;color:#0b3d91"><strong>E agora, o que acontece?</strong></p>
                  <ol style="margin:0 0 10px;padding-left:22px">
                    <li style="margin:0 0 6px">Sua solicitação já foi recebida.</li>
                    <li style="margin:0 0 6px">Um consultor entrará em contato para entender sua necessidade.</li>
                    <li style="margin:0 0 6px">Você receberá uma cotação com as condições disponíveis para seu veículo.</li>
                  </ol>
                  <p style="margin:0 0 6px"><strong>Fique de olho no seu WhatsApp!</strong> Nossa equipe poderá entrar em contato por lá.</p>
                  <p style="margin:0">Agradecemos pela confiança!<br><strong>Equipe Universo AGV</strong></p>
                </td></tr>
                <tr><td style="padding:20px 28px 8px"><div style="border-top:1px solid #e3e7ef"></div></td></tr>
                <tr><td align="center" style="padding:10px 28px 24px">
                  <table role="presentation" cellpadding="0" cellspacing="0" align="center">
                    <tr><td align="center">{avatar}</td></tr>
                    <tr><td align="center" style="padding-top:12px;font-size:16px;line-height:1.5">Você vai ser atendido por <strong>{e(nomeConsultor)}</strong></td></tr>
                    {botao}
                  </table>
                </td></tr>
                <tr><td style="padding:0 28px 22px;font-size:12px;line-height:1.5;color:#888888">
                  Os benefícios estão sujeitos às condições de aceitação, aos limites e ao regulamento aplicável.
                  {descadastro}
                </td></tr>
              </table>
            </td></tr></table>
            </body></html>
            """;
    }
}
