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
public record DiscordSincronizacaoDto(int CanaisCriados, int CargosCriados, int MembrosAtualizados, int MembrosForaDoServidor, IReadOnlyList<string> Falhas, int CanaisDeVozCriados = 0);

/// <summary>Grupo de conversa que o usuário pode abrir no chat do CRM.</summary>
/// <param name="Tipo"><c>grupo</c> ou <c>direta</c> (conversa 1:1: o nome é o da outra pessoa).</param>
public record DiscordChatCanalDto(string Chave, string Nome, string Tipo = "grupo", string? FotoUrl = null);

/// <summary>Pessoa com quem dá para iniciar uma conversa 1:1.</summary>
public record DiscordChatContatoDto(Guid Id, string Nome, string? FotoUrl, string? Regional);

public record DiscordChatIniciarRequest(Guid UsuarioId);

public record DiscordChatAnexoDto(string Nome, string Url, bool Imagem);

/// <param name="DoCrm">Publicada pelo CRM (a tela alinha as mensagens do próprio usuário à direita pelo nome do autor).</param>
public record DiscordChatMensagemDto(string Id, string AutorNome, string? AutorFotoUrl, string Conteudo, DateTimeOffset CriadaEm, IReadOnlyList<DiscordChatAnexoDto> Anexos, bool DoCrm);

/// <param name="TemMais">Pode haver mensagens mais antigas (pedir de novo com <c>antes</c>).</param>
/// <param name="ConteudoOculto">O Discord entregou as mensagens sem texto: falta ligar "Message Content Intent" no portal do desenvolvedor.</param>
public record DiscordChatMensagensDto(IReadOnlyList<DiscordChatMensagemDto> Mensagens, bool TemMais, bool ConteudoOculto);

public record DiscordChatEnviarRequest(string Texto);

/// <param name="Url">Endereço do canal de voz no Discord (abre o app ou o navegador).</param>
/// <param name="Avisou">O CRM publicou na conversa o aviso "fulano está numa chamada" com o link (não repete se a pessoa clicar de novo logo em seguida).</param>
public record DiscordChatChamadaDto(string Url, bool Avisou);

/// <param name="Total">Soma das mensagens não lidas de todas as conversas (o número do menu).</param>
/// <param name="PorConversa">Não lidas por conversa (chave da conversa → quantidade; só as que têm alguma). 50 significa "50 ou mais".</param>
public record DiscordChatNaoLidasDto(int Total, IReadOnlyDictionary<string, int> PorConversa);
