using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CanalDoDiscordNomeEAcessoPersonalizados : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcessoChave",
                table: "CrmDiscordCanais",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NomePersonalizado",
                table: "CrmDiscordCanais",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcessoChave",
                table: "CrmDiscordCanais");

            migrationBuilder.DropColumn(
                name: "NomePersonalizado",
                table: "CrmDiscordCanais");
        }
    }
}
