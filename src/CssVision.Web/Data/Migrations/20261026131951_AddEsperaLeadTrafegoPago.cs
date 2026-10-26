using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEsperaLeadTrafegoPago : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmEsperasLead",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultorId = table.Column<Guid>(type: "uuid", nullable: false),
                    EsperandoDesde = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    AvisoConsultorEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AvisoUrgenteEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtendidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmEsperasLead", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmEsperasLead_CrmLeads_LeadId",
                        column: x => x.LeadId,
                        principalTable: "CrmLeads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmEsperasLead_ConsultorId_AvisoUrgenteEm",
                table: "CrmEsperasLead",
                columns: new[] { "ConsultorId", "AvisoUrgenteEm" },
                filter: "\"AvisoUrgenteEm\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CrmEsperasLead_LeadId",
                table: "CrmEsperasLead",
                column: "LeadId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmEsperasLead");
        }
    }
}
