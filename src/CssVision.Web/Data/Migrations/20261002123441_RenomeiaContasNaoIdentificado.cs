using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>Contas de "vendedor não identificado" voltam ao nome padrão (pedido de 02/10/2026).</summary>
    public partial class RenomeiaContasNaoIdentificado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // As contas de "vendedor não identificado" (cards do Notion sem vendedor) tinham sido
            // renomeadas com nomes de pessoas — "Larissa", "Sabrina", "Deivison Gabriel" — e foram
            // confundidas com as contas reais (ver ReverteJuncaoContasNaoIdentificado). Identificadas
            // pelo e-mail de sistema, voltam ao nome padrão da regional.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" SET "NomeCompleto" = 'Vendedor não identificado (CSS Growth Sales)'
                WHERE "NormalizedEmail" = 'VENDEDOR.NAO.IDENTIFICADO.CSS-GROWTH-SALES@CSSVISION.LOCAL';

                UPDATE "AspNetUsers" SET "NomeCompleto" = 'Vendedor não identificado (MG132)'
                WHERE "NormalizedEmail" = 'VENDEDOR.NAO.IDENTIFICADO.MG132@CSSVISION.LOCAL';

                UPDATE "AspNetUsers" SET "NomeCompleto" = 'Vendedor não identificado (MG134)'
                WHERE "NormalizedEmail" = 'VENDEDOR.NAO.IDENTIFICADO.MG134@CSSVISION.LOCAL';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
