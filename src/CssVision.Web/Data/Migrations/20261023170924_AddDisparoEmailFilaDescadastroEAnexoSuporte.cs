using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDisparoEmailFilaDescadastroEAnexoSuporte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnexoNome",
                table: "CrmSuporteMensagens",
                type: "character varying(260)",
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AnexoUrl",
                table: "CrmSuporteMensagens",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmailDescadastradoEm",
                table: "CrmLeads",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CrmDisparosEmail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    EtapaId = table.Column<Guid>(type: "uuid", nullable: false),
                    EtapaNome = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Assunto = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Mensagem = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Enviados = table.Column<int>(type: "integer", nullable: false),
                    Falhas = table.Column<int>(type: "integer", nullable: false),
                    Erro = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IniciadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConcluidoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmDisparosEmail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrmDisparosEmailDestinatarios",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisparoId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeadId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Nome = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    EnviadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmDisparosEmailDestinatarios", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrmDisparosEmailDestinatarios_CrmDisparosEmail_DisparoId",
                        column: x => x.DisparoId,
                        principalTable: "CrmDisparosEmail",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmDisparosEmail_Status_CriadoEm",
                table: "CrmDisparosEmail",
                columns: new[] { "Status", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmDisparosEmail_UsuarioId_CriadoEm",
                table: "CrmDisparosEmail",
                columns: new[] { "UsuarioId", "CriadoEm" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmDisparosEmailDestinatarios_DisparoId_Status",
                table: "CrmDisparosEmailDestinatarios",
                columns: new[] { "DisparoId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmDisparosEmailDestinatarios");

            migrationBuilder.DropTable(
                name: "CrmDisparosEmail");

            migrationBuilder.DropColumn(
                name: "AnexoNome",
                table: "CrmSuporteMensagens");

            migrationBuilder.DropColumn(
                name: "AnexoUrl",
                table: "CrmSuporteMensagens");

            migrationBuilder.DropColumn(
                name: "EmailDescadastradoEm",
                table: "CrmLeads");
        }
    }
}
