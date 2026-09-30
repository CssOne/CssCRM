using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPushNotificacoes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmParametros",
                columns: table => new
                {
                    Chave = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Valor = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmParametros", x => x.Chave);
                });

            migrationBuilder.CreateTable(
                name: "CrmPushInscricoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Navegador = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    UltimoEnvioEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmPushInscricoes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmPushInscricoes_Endpoint",
                table: "CrmPushInscricoes",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmPushInscricoes_UsuarioId",
                table: "CrmPushInscricoes",
                column: "UsuarioId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmParametros");

            migrationBuilder.DropTable(
                name: "CrmPushInscricoes");
        }
    }
}
