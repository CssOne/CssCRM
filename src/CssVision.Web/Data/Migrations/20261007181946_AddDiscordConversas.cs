using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscordConversas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmDiscordConversas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioAId = table.Column<Guid>(type: "uuid", nullable: false),
                    UsuarioBId = table.Column<Guid>(type: "uuid", nullable: false),
                    DiscordThreadId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CriadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CriadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    AtualizadoEm = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AtualizadoPorId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmDiscordConversas", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrmDiscordConversas_UsuarioAId_UsuarioBId",
                table: "CrmDiscordConversas",
                columns: new[] { "UsuarioAId", "UsuarioBId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrmDiscordConversas");
        }
    }
}
