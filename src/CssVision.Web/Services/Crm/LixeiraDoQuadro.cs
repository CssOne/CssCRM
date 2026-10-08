using System.Linq.Expressions;
using CssVision.Web.Domain.Crm;

namespace CssVision.Web.Services.Crm;

/// <summary>
/// Lead excluído por alguém no CRM (arquivado por um usuário) fica só na lixeira do quadro de leads: some das demais telas, mesmo com o filtro
/// "inclui arquivados". Leads arquivados por outros meios (ex.: MG134) não são lixeira.
/// </summary>
public static class LixeiraDoQuadro
{
    public static readonly Expression<Func<CrmLead, bool>> ForaDaLixeira = l => !(l.Arquivado && l.ArquivadoPorId != null);
}
