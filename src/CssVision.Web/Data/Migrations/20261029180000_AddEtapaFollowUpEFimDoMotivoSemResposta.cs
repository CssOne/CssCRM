using CssVision.Web.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <summary>
    /// Nova coluna "Follow Up" no quadro de leads, logo depois de "Perdido"; os leads de "Perdido" com motivo "Já tem seguro" ou "Preço" saem de "Perdido" e vão para ela, mantendo o motivo (Follow Up exige "Já tem seguro" ou "Preço").
    /// O motivo "Sem resposta do cliente" deixa de existir: os leads que o tinham vão para a coluna "Não responde" (sem motivo de perda, como as
    /// demais colunas que não são "Perdido") e as oportunidades passam para o motivo "Não responde". O motivo "Fora da tabela de aceitação" também
    /// deixa de existir: os leads com ele vão para "Não fazemos" (sem veículo informado — não há como saber o modelo). O motivo "Concorrência" passa a se chamar "Fechado na Concorrência" e o lead perdido por ele pode ter a proposta do concorrente anexada (PropostaConcorrenteUrl).
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20261029180000_AddEtapaFollowUpEFimDoMotivoSemResposta")]
    public partial class AddEtapaFollowUpEFimDoMotivoSemResposta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(name: "SeguroVenceEm", table: "CrmLeads", type: "date", nullable: true);
            migrationBuilder.AddColumn<DateOnly>(name: "SeguroAvisoEnviadoEm", table: "CrmLeads", type: "date", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "SeguroAtividadeId", table: "CrmLeads", type: "uuid", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PropostaConcorrenteUrl",
                table: "CrmLeads",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "CrmLeadStages" SET "Ordem" = "Ordem" + 1
                WHERE "Ordem" > (SELECT "Ordem" FROM "CrmLeadStages" WHERE "Nome" = 'Perdido')
                  AND NOT EXISTS (SELECT 1 FROM "CrmLeadStages" WHERE "Nome" = 'Follow Up');

                INSERT INTO "CrmLeadStages" ("Id", "Nome", "Ordem", "Ativa", "Fechada", "Cor", "CriadoEm")
                SELECT gen_random_uuid(), 'Follow Up',
                       COALESCE((SELECT "Ordem" FROM "CrmLeadStages" WHERE "Nome" = 'Perdido'), (SELECT COALESCE(MAX("Ordem"), 0) FROM "CrmLeadStages")) + 1,
                       true, false, '#8b5cf6', now()
                WHERE NOT EXISTS (SELECT 1 FROM "CrmLeadStages" WHERE "Nome" = 'Follow Up');

                UPDATE "CrmLeadStages" SET "Ativa" = true WHERE "Nome" = 'Follow Up';

                INSERT INTO "CrmLossReasons" ("Id", "Descricao", "Ativo", "CriadoEm")
                SELECT gen_random_uuid(), novo.descricao, true, now()
                FROM (VALUES ('Já tem seguro'), ('Preço')) AS novo(descricao)
                WHERE NOT EXISTS (SELECT 1 FROM "CrmLossReasons" m WHERE lower(m."Descricao") = lower(novo.descricao));

                UPDATE "CrmLossReasons" SET "Ativo" = true WHERE lower("Descricao") IN ('já tem seguro', 'preço');

                UPDATE "CrmLeads"
                SET "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Follow Up'), "AtualizadoEm" = now()
                WHERE "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Perdido')
                  AND "MotivoPerdaId" IN (SELECT "Id" FROM "CrmLossReasons" WHERE lower("Descricao") IN ('já tem seguro', 'preço'));

                INSERT INTO "CrmLossReasons" ("Id", "Descricao", "Ativo", "CriadoEm")
                SELECT gen_random_uuid(), 'Não responde', true, now()
                WHERE NOT EXISTS (SELECT 1 FROM "CrmLossReasons" WHERE lower("Descricao") = 'não responde');

                UPDATE "CrmLeads"
                SET "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Não responde'),
                    "MotivoPerdaId" = NULL, "AtualizadoEm" = now()
                WHERE "MotivoPerdaId" IN (SELECT "Id" FROM "CrmLossReasons" WHERE "Descricao" = 'Sem resposta do cliente');

                UPDATE "CrmOpportunities"
                SET "MotivoPerdaId" = (SELECT "Id" FROM "CrmLossReasons" WHERE lower("Descricao") = 'não responde' LIMIT 1)
                WHERE "MotivoPerdaId" IN (SELECT "Id" FROM "CrmLossReasons" WHERE "Descricao" = 'Sem resposta do cliente');

                UPDATE "CrmLossReasons" SET "Ativo" = false WHERE "Descricao" = 'Sem resposta do cliente';

                UPDATE "CrmLeads"
                SET "EtapaId" = (SELECT "Id" FROM "CrmLeadStages" WHERE "Nome" = 'Não fazemos'),
                    "MotivoPerdaId" = NULL, "AtualizadoEm" = now()
                WHERE "MotivoPerdaId" IN (SELECT "Id" FROM "CrmLossReasons" WHERE "Descricao" = 'Fora da tabela de aceitação');

                UPDATE "CrmLossReasons" SET "Ativo" = false WHERE "Descricao" = 'Fora da tabela de aceitação';

                UPDATE "CrmLossReasons" SET "Descricao" = 'Fechado na Concorrência' WHERE "Descricao" = 'Concorrência';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "SeguroVenceEm", table: "CrmLeads");
            migrationBuilder.DropColumn(name: "SeguroAvisoEnviadoEm", table: "CrmLeads");
            migrationBuilder.DropColumn(name: "SeguroAtividadeId", table: "CrmLeads");

            migrationBuilder.DropColumn(
                name: "PropostaConcorrenteUrl",
                table: "CrmLeads");

            // Sem volta: não dá para saber quais leads vieram de "Perdido" ou do motivo removido.
            migrationBuilder.Sql("""
                UPDATE "CrmLeadStages" SET "Ativa" = false WHERE "Nome" = 'Follow Up';
                UPDATE "CrmLossReasons" SET "Ativo" = true WHERE "Descricao" IN ('Sem resposta do cliente', 'Fora da tabela de aceitação');
                """);
        }
    }
}
