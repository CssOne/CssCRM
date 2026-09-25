using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SessaoPersistenteERecebeSomenteOQue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecebeSomenteOQue",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });
            // Samys Alexandre (consultora de caminhão, dois logins) recebe só leads "AGV TRUCK" na distribuição automática.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" SET "RecebeSomenteOQue" = 'AGV TRUCK'
                WHERE "NormalizedEmail" IN ('SAMYS.ALEXANDRE@GMAIL.COM', 'SAMYS.FINANCEIRO@GMAIL.COM');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropColumn(
                name: "RecebeSomenteOQue",
                table: "AspNetUsers");
        }
    }
}
