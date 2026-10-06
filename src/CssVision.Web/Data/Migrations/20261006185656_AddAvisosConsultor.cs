using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAvisosConsultor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmAvisosConsultor",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConsultorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Titulo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Valor = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    Referencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EnviadoPorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    LidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ResolvidoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmAvisosConsultor", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmAvisosConsultor_ConsultorId_Status",
                table: "CrmAvisosConsultor",
                columns: new[] { "ConsultorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmAvisosConsultor_CriadoEm",
                table: "CrmAvisosConsultor",
                column: "CriadoEm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmAvisosConsultor");
        }
    }
}
