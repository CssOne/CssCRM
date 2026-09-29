using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVeiculoAdicionalDeLead : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "VeiculoAdicionalDeLeadId",
                table: "CrmLeads",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrmLeads_VeiculoAdicionalDeLeadId",
                table: "CrmLeads",
                column: "VeiculoAdicionalDeLeadId");

            migrationBuilder.AddForeignKey(
                name: "FK_CrmLeads_CrmLeads_VeiculoAdicionalDeLeadId",
                table: "CrmLeads",
                column: "VeiculoAdicionalDeLeadId",
                principalTable: "CrmLeads",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CrmLeads_CrmLeads_VeiculoAdicionalDeLeadId",
                table: "CrmLeads");

            migrationBuilder.DropIndex(
                name: "IX_CrmLeads_VeiculoAdicionalDeLeadId",
                table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "VeiculoAdicionalDeLeadId",
                table: "CrmLeads");
        }
    }
}
