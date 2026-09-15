namespace CssVision.Web.Domain.Crm;

public enum TipoAnuncio
{
    Aviso = 1,
    Flashcard = 2,
}

/// <summary>
/// Comunicado exibido no Portal do Consultor (seções "Avisos importantes" e "Flashcards" do
/// dashboard). Sem UI de gestão ainda — hoje só é semeado via CrmSeeder; um CRUD administrativo
/// é um ponto de extensão natural quando o conteúdo passar a mudar com frequência.
/// </summary>
public class CrmAnnouncement : CrmEntityBase
{
    public TipoAnuncio Tipo { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string Descricao { get; set; } = string.Empty;

    /// <summary>Token de cor usado nos flashcards (ex: "blue", "green", "amber", "purple"). Ignorado para Aviso.</summary>
    public string? Cor { get; set; }

    public bool Ativo { get; set; } = true;

    /// <summary>Define a ordem de exibição (menor primeiro).</summary>
    public int Ordem { get; set; }
}
