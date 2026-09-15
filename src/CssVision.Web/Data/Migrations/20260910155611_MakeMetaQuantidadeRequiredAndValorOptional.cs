using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeMetaQuantidadeRequiredAndValorOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "MetaValor",
                table: "CrmSalesGoals",
                type: "numeric(14,2)",
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)");

            migrationBuilder.Sql(
                "UPDATE \"CrmSalesGoals\" SET \"MetaQuantidadeVendas\" = 0 WHERE \"MetaQuantidadeVendas\" IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "MetaQuantidadeVendas",
                table: "CrmSalesGoals",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "MetaValor",
                table: "CrmSalesGoals",
                type: "numeric(14,2)",
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "MetaQuantidadeVendas",
                table: "CrmSalesGoals",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
