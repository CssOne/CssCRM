using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <summary>
    /// "Não responde" é coluna do quadro de leads, não motivo de perda: os leads que ainda tinham esse motivo (em "Perdido") vão para a coluna
    /// "Não responde", sem motivo de perda, como as demais colunas fora de "Perdido". O motivo continua cadastrado (as oportunidades do Pipeline o usam),
    /// mas "Perdido" do quadro deixa de oferecê-lo.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261030190000_LevaLeadsComMotivoNaoRespondeParaAColuna")]
    public partial class LevaLeadsComMotivoNaoRespondeParaAColuna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "CrmLeads"
                SET "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Não responde' AND "Ativa" LIMIT 1),
                    "MotivoPerdaId" = NULL, "MotivoPerdaObservacao" = NULL, "AtualizadoEm" = now()
                WHERE "MotivoPerdaId" IN (SELECT "Id" FROM "CrmLossReasons" WHERE lower("Descricao") = 'não responde')
                  AND EXISTS (SELECT 1 FROM "CrmLeadStages" WHERE "Nome" = 'Não responde' AND "Ativa");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta: não dá para saber quais leads vieram de "Perdido" com esse motivo.
        }
    }
}
