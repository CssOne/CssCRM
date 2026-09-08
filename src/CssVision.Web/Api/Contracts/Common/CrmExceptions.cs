namespace CssVision.Web.Api.Contracts.Common;

/// <summary>Usuário autenticado tentando acessar/alterar um registro fora da sua carteira ou equipe.</summary>
public class CrmForbiddenException(string mensagem = "Você não tem permissão para acessar este registro.")
    : Exception(mensagem);

public class CrmNotFoundException(string entidade, Guid id)
    : Exception($"{entidade} '{id}' não encontrado(a).");

/// <summary>Conflito de concorrência otimista: o registro foi alterado por outro usuário.</summary>
public class CrmConcurrencyException(string mensagem = "Este registro foi alterado por outro usuário. Recarregue e tente novamente.")
    : Exception(mensagem);
