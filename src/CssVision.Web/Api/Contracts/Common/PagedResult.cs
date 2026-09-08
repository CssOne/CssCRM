namespace CssVision.Web.Api.Contracts.Common;

public class PagedResult<T>
{
    public IReadOnlyList<T> Itens { get; init; } = [];
    public int Pagina { get; init; }
    public int TamanhoPagina { get; init; }
    public int TotalRegistros { get; init; }
    public int TotalPaginas => TamanhoPagina == 0 ? 0 : (int)Math.Ceiling(TotalRegistros / (double)TamanhoPagina);
}

public record PagedRequest
{
    private const int TamanhoPaginaMaximo = 100;
    private int _pagina = 1;
    private int _tamanhoPagina = 20;

    public int Pagina
    {
        get => _pagina;
        set => _pagina = value < 1 ? 1 : value;
    }

    public int TamanhoPagina
    {
        get => _tamanhoPagina;
        set => _tamanhoPagina = value switch
        {
            < 1 => 20,
            > TamanhoPaginaMaximo => TamanhoPaginaMaximo,
            _ => value
        };
    }
}
