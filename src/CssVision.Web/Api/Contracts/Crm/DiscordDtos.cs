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
public record DiscordSincronizacaoDto(int CanaisCriados, int CargosCriados, int MembrosAtualizados, int MembrosForaDoServidor, IReadOnlyList<string> Falhas);
