using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmLeadCriadoManualmente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CriadoManualmente",
                table: "CrmLeads",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_CriadoManualmente",
                table: "CrmLeads",
                column: "CriadoManualmente");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_CriadoManualmente",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "CriadoManualmente",
                table: "CrmLeads");
        }
    }
}
