using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class VinculoLeadCardNotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotionPageId",
                table: "CrmLeads",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_NotionPageId",
                table: "CrmLeads",
                column: "NotionPageId",
                unique: true,
                filter: "\"NotionPageId\" IS NOT NULL");

            // Refaz o realinhamento com a busca corrigida (vínculo com o card, CPF só se válido,
            // telefone do [META] quando o WhatsApp não é número): corrige leads que ficaram em
            // "Sem etapa" ou que receberam o Status do card de outro cliente.
            migrationBuilder.Sql("""UPDATE "CrmNotionSyncCheckpoints" SET "RealinhamentoConcluidoEm" = NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_NotionPageId",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "NotionPageId",
                table: "CrmLeads");
        }
    }
}
