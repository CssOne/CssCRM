namespace CssVision.Web.Api.Contracts.Crm;

/// <summary>Situação da integração com o Discord para o usuário logado.</summary>
public record DiscordStatusDto(
    /// <summary>O servidor tem as credenciais do Discord. Sem isso a tela só avisa que a integração não foi ativada.</summary>
    bool Configurado,
    bool Vinculado,
    string? DiscordNome,
    bool AvisosAtivos,
    /// <summary>A conta do usuário está no servidor da empresa (o bot a colocou lá ao vincular).</summary>
    bool NoServidor,
    DateTimeOffset? VinculadoEm);

public record DiscordIniciarDto(string Url);

public record DiscordAvisosRequest(bool Ativos);

public record DiscordTesteDto(bool Enviado, string Mensagem);

/// <summary>Grupo do CRM que existe como canal no Discord.</summary>
public record DiscordCanalDto(string Chave, string Nome, bool Ativo);

/// <summary>O que a sincronização dos grupos fez, e o que não conseguiu fazer (com o motivo).</summary>
public record DiscordSincronizacaoDto(int CanaisCriados, int CargosCriados, int MembrosAtualizados, int MembrosForaDoServidor, IReadOnlyList<string> Falhas, int CanaisDeVozCriados = 0, int ApelidosDefinidos = 0, bool BoasVindasPublicadas = false);

/// <summary>Grupo de conversa que o usuário pode abrir no chat do CRM.</summary>
/// <param name="Tipo"><c>grupo</c> ou <c>direta</c> (conversa 1:1: o nome é o da outra pessoa).</param>
/// <param name="Online">Só nas conversas 1:1: a outra pessoa está com o CRM aberto agora.</param>
public record DiscordChatCanalDto(string Chave, string Nome, string Tipo = "grupo", string? FotoUrl = null, bool Online = false);

/// <summary>Pessoa com quem dá para iniciar uma conversa 1:1.</summary>
public record DiscordChatContatoDto(Guid Id, string Nome, string? FotoUrl, string? Regional, bool Online = false);

public record DiscordChatIniciarRequest(Guid UsuarioId);

public record DiscordChatAnexoDto(string Nome, string Url, bool Imagem);

/// <param name="DoCrm">Publicada pelo CRM (a tela alinha as mensagens do próprio usuário à direita pelo nome do autor).</param>
public record DiscordChatMensagemDto(string Id, string AutorNome, string? AutorFotoUrl, string Conteudo, DateTimeOffset CriadaEm, IReadOnlyList<DiscordChatAnexoDto> Anexos, bool DoCrm, bool Editada = false, DiscordChatEnqueteDto? Enquete = null);

/// <summary>Enquete da mensagem, só para mostrar (votar é no Discord).</summary>
public record DiscordChatEnqueteDto(string Pergunta, IReadOnlyList<DiscordChatRespostaDto> Respostas, bool VariasEscolhas, DateTimeOffset? EncerraEm, bool Encerrada);

public record DiscordChatRespostaDto(string Texto, int Votos);

public record DiscordChatTopicoRequest(string Nome);

public record DiscordChatEnqueteRequest(string Pergunta, IReadOnlyList<string> Respostas, int Horas = 24, bool VariasEscolhas = false);

/// <param name="TemMais">Pode haver mensagens mais antigas (pedir de novo com <c>antes</c>).</param>
/// <param name="ConteudoOculto">O Discord entregou as mensagens sem texto: falta ligar "Message Content Intent" no portal do desenvolvedor.</param>
/// <summary>Emoji personalizado do servidor para o seletor do chat (no texto vai como <c>&lt;:nome:id&gt;</c>).</summary>
public record DiscordChatEmojiDto(string Id, string Nome, bool Animado, string Url);

public record DiscordChatFigurinhaDto(string Id, string Nome, string Url);

/// <summary>O que o seletor do chat oferece além dos emojis comuns: emojis e figurinhas do servidor.</summary>
public record DiscordChatExtrasDto(IReadOnlyList<DiscordChatEmojiDto> Emojis, IReadOnlyList<DiscordChatFigurinhaDto> Figurinhas);

public record DiscordChatFigurinhaRequest(string FigurinhaId);

public record DiscordChatMensagensDto(IReadOnlyList<DiscordChatMensagemDto> Mensagens, bool TemMais, bool ConteudoOculto);

/// <param name="Mencoes">Pessoas escolhidas na lista de menção (o texto traz "@Nome" de cada uma); só entram as que vincularam o Discord.</param>
public record DiscordChatEnviarRequest(string Texto, IReadOnlyList<Guid>? Mencoes = null);

public record DiscordChatEditarRequest(string Texto);

/// <param name="Comentario">Texto opcional da pessoa, publicado junto do lead (até 500 caracteres).</param>
public record DiscordChatLeadRequest(Guid LeadId, string? Comentario = null);

/// <summary>Quais avisos automáticos o CRM publica nos canais das regionais (todos desligados até o administrador ligar).</summary>
/// <param name="Venda">"Fulano fechou uma venda".</param>
/// <param name="MetaBatida">"A regional bateu a meta do mês" (uma vez por mês).</param>
/// <param name="LeadsParados">Resumo diário (depois das 9h30) de quantos leads de anúncio estão parados há mais de 5 dias.</param>
public record DiscordAvisosCanaisDto(bool Venda, bool MetaBatida, bool LeadsParados);

/// <summary>Quem está online (com o CRM aberto) numa conversa.</summary>
public record DiscordChatOnlineDto(IReadOnlyList<DiscordChatPessoaOnlineDto> Pessoas);

public record DiscordChatPessoaOnlineDto(Guid Id, string Nome, string? FotoUrl);

/// <param name="Url">Endereço do canal de voz no Discord (abre o app ou o navegador).</param>
/// <param name="Avisou">O CRM publicou na conversa o aviso "fulano está numa chamada" com o link (não repete se a pessoa clicar de novo logo em seguida).</param>
public record DiscordChatChamadaDto(string Url, bool Avisou);

/// <param name="Total">Soma das mensagens não lidas de todas as conversas (o número do menu).</param>
/// <param name="PorConversa">Não lidas por conversa (chave da conversa → quantidade; só as que têm alguma). 50 significa "50 ou mais".</param>
public record DiscordChatNaoLidasDto(int Total, IReadOnlyDictionary<string, int> PorConversa);
