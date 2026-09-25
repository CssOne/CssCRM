using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RevisaoTipoIndicacaoNotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Relê as bases do Notion de novo (reimportação) para preencher o tipo de indicação dos
            // leads já importados a partir dos campos "Indicação?" e "Tipo de Indicação?" dos cards.
            migrationBuilder.Sql("""UPDATE "CrmNotionSyncCheckpoints" SET "ReimportacaoAtivosConcluidaEm" = NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
