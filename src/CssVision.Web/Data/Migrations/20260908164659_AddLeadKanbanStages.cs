using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadKanbanStages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_Status",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CrmLeads");

            migrationBuilder.AddColumn<Guid>(
                name: "EtapaId",
                table: "CrmLeads",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "CrmLeadStages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Nome = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Ordem = table.Column<int>(type: "integer", nullable: false),
                    Ativa = table.Column<bool>(type: "boolean", nullable: false),
                    Fechada = table.Column<bool>(type: "boolean", nullable: false),
                    Cor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmLeadStages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_EtapaId",
                table: "CrmLeads",
                column: "EtapaId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeadStages_Ativa",
                table: "CrmLeadStages",
                column: "Ativa");

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeadStages_Ordem",
                table: "CrmLeadStages",
                column: "Ordem");

            migrationBuilder.AddForeignKey(
                name: "FK_CrmLeads_CrmLeadStages_EtapaId",
                table: "CrmLeads",
                column: "EtapaId",
                principalTable: "CrmLeadStages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CrmLeads_CrmLeadStages_EtapaId",
                table: "CrmLeads");

            migrationBuilder.DropTable(
                name: "CrmLeadStages");

            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_EtapaId",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "EtapaId",
                table: "CrmLeads");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "CrmLeads",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_Status",
                table: "CrmLeads",
                column: "Status");
        }
    }
}
