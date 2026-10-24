namespace CssVision.Web.Authorization;

/// <summary>Nomes das políticas de limite de requisições (ver Extensions/SecurityExtensions).</summary>
public static class RateLimitPolicies
{
    /// <summary>Login e segunda etapa (2FA): trava força bruta de senha/código por IP.</summary>
    public const string Login = "login";

    /// <summary>"Esqueci a senha" e redefinição: impede usar o sistema para encher a caixa de e-mail de alguém.</summary>
    public const string RecuperarSenha = "recuperar-senha";

    /// <summary>Formulários públicos de lead (sem login): limita spam e cadastro em massa.</summary>
    public const string FormularioPublico = "formulario-publico";

    /// <summary>Gravações do Controle financeiro (por usuário logado).</summary>
    public const string FinanceiroEscrita = "financeiro-escrita";
}
