using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmVeiculoAndMarketingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AtivoEm",
                table: "CrmOpportunities",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DataAdesao",
                table: "CrmOpportunities",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Mensalidade",
                table: "CrmOpportunities",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MensalidadeComDesconto",
                table: "CrmOpportunities",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Migracao",
                table: "CrmOpportunities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "PagamentoAdesao",
                table: "CrmOpportunities",
                type: "numeric(14,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Porcentagem",
                table: "CrmOpportunities",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TermoAdesaoAceito",
                table: "CrmOpportunities",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Gclid",
                table: "CrmLeads",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "IndicadoPorLeadId",
                table: "CrmLeads",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaClickId",
                table: "CrmLeads",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaFormId",
                table: "CrmLeads",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaLeadId",
                table: "CrmLeads",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TipoIndicacao",
                table: "CrmLeads",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmMedium",
                table: "CrmLeads",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmSource",
                table: "CrmLeads",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtmTerm",
                table: "CrmLeads",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrmVeiculos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Descricao = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Placa = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Fipe = table.Column<decimal>(type: "numeric(14,2)", nullable: true),
                    Rastreador = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    VistoriadorId = table.Column<Guid>(type: "uuid", nullable: true),
                    DataChegada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmVeiculos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmVeiculos_AspNetUsers_VistoriadorId",
                        column: x => x.VistoriadorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CrmVeiculos_CrmOpportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "CrmOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_IndicadoPorLeadId",
                table: "CrmLeads",
                column: "IndicadoPorLeadId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmVeiculos_OpportunityId",
                table: "CrmVeiculos",
                column: "OpportunityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmVeiculos_Placa",
                table: "CrmVeiculos",
                column: "Placa");

            migrationBuilder.CreateIndex(
                name: "IX_CrmVeiculos_VistoriadorId",
                table: "CrmVeiculos",
                column: "VistoriadorId");

            migrationBuilder.AddForeignKey(
                name: "FK_CrmLeads_CrmLeads_IndicadoPorLeadId",
                table: "CrmLeads",
                column: "IndicadoPorLeadId",
                principalTable: "CrmLeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CrmLeads_CrmLeads_IndicadoPorLeadId",
                table: "CrmLeads");

            migrationBuilder.DropTable(
                name: "CrmVeiculos");

            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_IndicadoPorLeadId",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "AtivoEm",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "DataAdesao",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "Mensalidade",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "MensalidadeComDesconto",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "Migracao",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "PagamentoAdesao",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "Porcentagem",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "TermoAdesaoAceito",
                table: "CrmOpportunities");

            migrationBuilder.DropColumn(
                name: "Gclid",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "IndicadoPorLeadId",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "MetaClickId",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "MetaFormId",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "MetaLeadId",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "TipoIndicacao",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "UtmMedium",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "UtmSource",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "UtmTerm",
                table: "CrmLeads");
        }
    }
}
