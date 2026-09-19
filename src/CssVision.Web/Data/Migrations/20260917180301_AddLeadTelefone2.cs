using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLeadTelefone2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Telefone2",
                table: "CrmLeads",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telefone2Normalizado",
                table: "CrmLeads",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Telefone2",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "Telefone2Normalizado",
                table: "CrmLeads");
        }
    }
}
