using CssVision.Web.Api.Contracts.Crm;
using CssVision.Web.Services.Crm;
using CssVision.Web.Services.Discord;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CssVision.Web.Api.Controllers;

/// <summary>Chat de texto dos grupos da empresa (as mensagens ficam no Discord; o CRM mostra e publica).</summary>
[ApiController]
[Route("api/crm/discord/chat")]
[Authorize]
public class CrmDiscordChatController(IDiscordChatService chat, ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet("canais")]
    public async Task<ActionResult<IReadOnlyList<DiscordChatCanalDto>>> Canais(CancellationToken ct) =>
        Ok(await chat.ListarCanaisAsync(currentUser.UserId, ct));

    /// <summary>Abre um tópico no grupo (e avisa o grupo com o link).</summary>
    [HttpPost("canais/{chave}/topicos")]
    public async Task<ActionResult<DiscordChatMensagemDto>> CriarTopico(string chave, [FromBody] DiscordChatTopicoRequest request, CancellationToken ct) =>
        Ok(await chat.CriarTopicoAsync(currentUser.UserId, chave, request.Nome, ct));

    /// <summary>Publica uma enquete na conversa.</summary>
    [HttpPost("canais/{chave}/enquetes")]
    public async Task<ActionResult<DiscordChatMensagemDto>> CriarEnquete(string chave, [FromBody] DiscordChatEnqueteRequest request, CancellationToken ct) =>
        Ok(await chat.CriarEnqueteAsync(currentUser.UserId, chave, request.Pergunta, request.Respostas, request.Horas, request.VariasEscolhas, ct));

    /// <summary>Reage (ou tira a reação) a uma mensagem; devolve as reações atualizadas dela.</summary>
    [HttpPost("canais/{chave}/mensagens/{mensagemId}/reacoes")]
    public async Task<ActionResult<IReadOnlyList<DiscordChatReacaoDto>>> Reagir(string chave, string mensagemId, [FromBody] DiscordChatReacaoRequest request, CancellationToken ct) =>
        Ok(await chat.AlternarReacaoAsync(currentUser.UserId, chave, mensagemId, request.Emoji, ct));

    /// <summary>Fixa ou desafixa uma mensagem (só gestores e administradores).</summary>
    [HttpPut("canais/{chave}/mensagens/{mensagemId}/fixada")]
    public async Task<IActionResult> Fixar(string chave, string mensagemId, [FromBody] DiscordChatFixarRequest request, CancellationToken ct)
    {
        await chat.FixarMensagemAsync(currentUser.UserId, chave, mensagemId, request.Fixar, ct);
        return NoContent();
    }

    [HttpGet("canais/{chave}/fixadas")]
    public async Task<ActionResult<IReadOnlyList<DiscordChatMensagemDto>>> Fixadas(string chave, CancellationToken ct) =>
        Ok(await chat.ListarFixadasAsync(currentUser.UserId, chave, ct));

    /// <summary>Procura um texto nas últimas mensagens da conversa.</summary>
    [HttpGet("canais/{chave}/busca")]
    public async Task<ActionResult<IReadOnlyList<DiscordChatMensagemDto>>> Buscar(string chave, [FromQuery] string termo, CancellationToken ct) =>
        Ok(await chat.BuscarMensagensAsync(currentUser.UserId, chave, termo, ct));

    /// <summary>Tópicos ativos do grupo.</summary>
    [HttpGet("canais/{chave}/topicos")]
    public async Task<ActionResult<IReadOnlyList<DiscordChatTopicoDto>>> Topicos(string chave, CancellationToken ct) =>
        Ok(await chat.ListarTopicosAsync(currentUser.UserId, chave, ct));

    /// <summary>Emojis e figurinhas do servidor para o seletor do chat.</summary>
    [HttpGet("extras")]
    public async Task<ActionResult<DiscordChatExtrasDto>> Extras(CancellationToken ct) =>
        Ok(await chat.ListarExtrasAsync(ct));

    /// <summary>Envia uma figurinha do servidor para a conversa.</summary>
    [HttpPost("canais/{chave}/figurinhas")]
    public async Task<ActionResult<DiscordChatMensagemDto>> EnviarFigurinha(string chave, [FromBody] DiscordChatFigurinhaRequest request, CancellationToken ct) =>
        Ok(await chat.EnviarFigurinhaAsync(currentUser.UserId, chave, request.FigurinhaId, ct));

    /// <summary>Mensagens não lidas de cada conversa (para o número do menu e as marcas na lista). Barato: pode ser chamado a cada poucos segundos.</summary>
    [HttpGet("nao-lidas")]
    public async Task<ActionResult<DiscordChatNaoLidasDto>> NaoLidas(CancellationToken ct) =>
        Ok(await chat.ContarNaoLidasAsync(currentUser.UserId, ct));

    /// <summary>Envia um arquivo (imagem, PDF, planilha...) para a conversa, com legenda opcional. Até 10 MB.</summary>
    [HttpPost("canais/{chave}/anexos")]
    [RequestSizeLimit(12 * 1024 * 1024)]
    public async Task<ActionResult<DiscordChatMensagemDto>> EnviarArquivo(string chave, IFormFile arquivo, [FromForm] string? texto, CancellationToken ct)
    {
        // Confere o tamanho antes de ler para a memória: um arquivo enorme é recusado sem ocupar o servidor.
        if (arquivo.Length > DiscordChatService.LimiteDoArquivo)
        {
            throw new CssVision.Web.Api.Contracts.Common.CrmBusinessException($"O arquivo pode ter no máximo {DiscordChatService.LimiteDoArquivo / (1024 * 1024)} MB.", "arquivo_grande");
        }

        await using var memoria = new MemoryStream();
        await arquivo.CopyToAsync(memoria, ct);
        return Ok(await chat.EnviarArquivoAsync(currentUser.UserId, chave, texto, arquivo.FileName, arquivo.ContentType, memoria.ToArray(), ct));
    }

    /// <summary>Endereço do canal de voz da conversa no Discord; também avisa a conversa de que a pessoa está numa chamada.</summary>
    [HttpPost("canais/{chave}/chamada")]
    public async Task<ActionResult<DiscordChatChamadaDto>> Chamada(string chave, CancellationToken ct) =>
        Ok(await chat.IniciarChamadaAsync(currentUser.UserId, chave, ct));

    /// <summary>Quem está com o CRM aberto agora nesta conversa.</summary>
    [HttpGet("canais/{chave}/online")]
    public async Task<ActionResult<DiscordChatOnlineDto>> Online(string chave, CancellationToken ct) =>
        Ok(await chat.ListarOnlineAsync(currentUser.UserId, chave, ct));

    /// <summary>Pessoas com quem dá para iniciar uma conversa 1:1 (quem vinculou o Discord e já está no servidor).</summary>
    [HttpGet("contatos")]
    public async Task<ActionResult<IReadOnlyList<DiscordChatContatoDto>>> Contatos([FromQuery] string? busca, CancellationToken ct) =>
        Ok(await chat.ListarContatosAsync(currentUser.UserId, busca, ct));

    /// <summary>Abre (criando, se for a primeira vez) a conversa 1:1 com a pessoa.</summary>
    [HttpPost("conversas")]
    public async Task<ActionResult<DiscordChatCanalDto>> IniciarConversa(DiscordChatIniciarRequest request, CancellationToken ct) =>
        Ok(await chat.IniciarConversaAsync(currentUser.UserId, request.UsuarioId, ct));

    [HttpGet("canais/{chave}/mensagens")]
    public async Task<ActionResult<DiscordChatMensagensDto>> Mensagens(string chave, [FromQuery] string? antes, CancellationToken ct) =>
        Ok(await chat.ListarMensagensAsync(currentUser.UserId, chave, antes, ct));

    [HttpPost("canais/{chave}/mensagens")]
    public async Task<ActionResult<DiscordChatMensagemDto>> Enviar(string chave, DiscordChatEnviarRequest request, CancellationToken ct) =>
        Ok(await chat.EnviarAsync(currentUser.UserId, chave, request.Texto, ct, request.Mencoes));

    /// <summary>Compartilha um lead (resumo + link) na conversa, para a equipe conversar sobre ele.</summary>
    [HttpPost("canais/{chave}/lead")]
    public async Task<ActionResult<DiscordChatMensagemDto>> CompartilharLead(string chave, DiscordChatLeadRequest request, CancellationToken ct) =>
        Ok(await chat.CompartilharLeadAsync(currentUser.UserId, chave, request.LeadId, request.Comentario, ct));

    /// <summary>Edita uma mensagem da própria pessoa (publicada pelo CRM).</summary>
    [HttpPut("canais/{chave}/mensagens/{mensagemId}")]
    public async Task<IActionResult> Editar(string chave, string mensagemId, DiscordChatEditarRequest request, CancellationToken ct)
    {
        await chat.EditarMensagemAsync(currentUser.UserId, chave, mensagemId, request.Texto, ct);
        return NoContent();
    }

    /// <summary>Apaga uma mensagem da própria pessoa (publicada pelo CRM).</summary>
    [HttpDelete("canais/{chave}/mensagens/{mensagemId}")]
    public async Task<IActionResult> Apagar(string chave, string mensagemId, CancellationToken ct)
    {
        await chat.ApagarMensagemAsync(currentUser.UserId, chave, mensagemId, ct);
        return NoContent();
    }
}
