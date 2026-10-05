using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSuporteChamados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmSuporteChamados",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Assunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Categoria = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SolicitanteId = table.Column<Guid>(type: "uuid", nullable: false),
                    UltimaMensagemEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmSuporteChamados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmSuporteMensagens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChamadoId = table.Column<Guid>(type: "uuid", nullable: false),
                    AutorId = table.Column<Guid>(type: "uuid", nullable: false),
                    DoSuporte = table.Column<bool>(type: "boolean", nullable: false),
                    Texto = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmSuporteMensagens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmSuporteMensagens_CrmSuporteChamados_ChamadoId",
                        column: x => x.ChamadoId,
                        principalTable: "CrmSuporteChamados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmSuporteChamados_SolicitanteId",
                table: "CrmSuporteChamados",
                column: "SolicitanteId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmSuporteChamados_Status_UltimaMensagemEm",
                table: "CrmSuporteChamados",
                columns: new[] { "Status", "UltimaMensagemEm" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmSuporteMensagens_ChamadoId",
                table: "CrmSuporteMensagens",
                column: "ChamadoId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmSuporteMensagens");

            migrationBuilder.DropTable(
                name: "CrmSuporteChamados");
        }
    }
}
