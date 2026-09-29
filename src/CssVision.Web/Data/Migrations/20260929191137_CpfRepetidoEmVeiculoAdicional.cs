using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CpfRepetidoEmVeiculoAdicional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_DocumentoNormalizado",
                table: "CrmLeads");

            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_EmailNormalizado",
                table: "CrmLeads");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_DocumentoNormalizado",
                table: "CrmLeads",
                column: "DocumentoNormalizado",
                unique: true,
                filter: "\"DocumentoNormalizado\" IS NOT NULL AND \"Arquivado\" = false AND \"VeiculoAdicionalDeLeadId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_EmailNormalizado",
                table: "CrmLeads",
                column: "EmailNormalizado",
                unique: true,
                filter: "\"EmailNormalizado\" IS NOT NULL AND \"Arquivado\" = false AND \"VeiculoAdicionalDeLeadId\" IS NULL");

            // Cards de veículo adicional criados antes desta regra ficaram sem CPF/e-mail: copia do card principal.
            migrationBuilder.Sql("""
                UPDATE "CrmLeads" AS adicional
                SET "DocumentoNormalizado" = coalesce(adicional."DocumentoNormalizado", principal."DocumentoNormalizado"),
                    "Email" = coalesce(adicional."Email", principal."Email"),
                    "EmailNormalizado" = coalesce(adicional."EmailNormalizado", principal."EmailNormalizado")
                FROM "CrmLeads" AS principal
                WHERE adicional."VeiculoAdicionalDeLeadId" = principal."Id"
                  AND (adicional."DocumentoNormalizado" IS NULL OR adicional."EmailNormalizado" IS NULL);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_DocumentoNormalizado",
                table: "CrmLeads");

            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_EmailNormalizado",
                table: "CrmLeads");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_DocumentoNormalizado",
                table: "CrmLeads",
                column: "DocumentoNormalizado",
                unique: true,
                filter: "\"DocumentoNormalizado\" IS NOT NULL AND \"Arquivado\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_EmailNormalizado",
                table: "CrmLeads",
                column: "EmailNormalizado",
                unique: true,
                filter: "\"EmailNormalizado\" IS NOT NULL AND \"Arquivado\" = false");
        }
    }
}
