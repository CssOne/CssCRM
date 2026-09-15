using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmLeadPlacaSeguroUtilidade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Placa",
                table: "CrmLeads",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TemSeguro",
                table: "CrmLeads",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UtilidadeVeiculo",
                table: "CrmLeads",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Placa",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "TemSeguro",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "UtilidadeVeiculo",
                table: "CrmLeads");
        }
    }
}
