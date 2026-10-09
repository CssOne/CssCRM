using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReacoesDoChatDoDiscord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmDiscordReacoes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MensagemId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LeituraId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    UsuarioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Emoji = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmDiscordReacoes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmDiscordReacoes_LeituraId",
                table: "CrmDiscordReacoes",
                column: "LeituraId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmDiscordReacoes_MensagemId_UsuarioId_Emoji",
                table: "CrmDiscordReacoes",
                columns: new[] { "MensagemId", "UsuarioId", "Emoji" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmDiscordReacoes");
        }
    }
}
