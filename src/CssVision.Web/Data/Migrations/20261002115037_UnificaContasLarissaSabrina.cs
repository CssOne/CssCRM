using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <summary>
    /// Junta as contas provisórias criadas na migração do Notion (e-mail @cssvision.local, sem login)
    /// às contas reais das mesmas pessoas, a pedido da gestão em 02/10/2026:
    /// <list type="bullet">
    /// <item>Larissa: 01a0a5e9-03a9-… (provisória, 4.592 leads e 532 vendas) → 01a0a5e9-01dd-… (gmail)</item>
    /// <item>Sabrina: 01a0a5e6-8e8d-… (provisória, 4.053 leads e 4.000 vendas) → 01a0ab0d-300e-… (gmail)</item>
    /// </list>
    /// Leads e vendas passam para a conta real sem mudar a data de atribuição (não dispara aviso de
    /// lead novo). As contas provisórias continuam existindo (inativas, sem nada), com o nome marcando
    /// a unificação; a conta real da Sabrina, que estava com o e-mail no lugar do nome, passa a "Sabrina".
    /// Idempotente: rodar de novo não muda nada.
    /// </summary>
    public partial class UnificaContasLarissaSabrina : Migration
    {
        private static readonly (string Origem, string Destino)[] Pares =
        [
            ("01a0a5e9-03a9-72eb-88f3-c232dbc22ce0", "01a0a5e9-01dd-7b72-be60-be586c5a2ee4"), // Larissa
            ("01a0a5e6-8e8d-7f80-a5f4-ca9c8b281d7a", "01a0ab0d-300e-7391-9599-88575a5e9e51"), // Sabrina
        ];

        /// <summary>Colunas de negócio que guardam o usuário (a auditoria fica como está).</summary>
        private static readonly (string Tabela, string Coluna)[] Colunas =
        [
            ("CrmLeads", "ResponsavelId"),
            ("CrmOpportunities", "ResponsavelId"),
            ("CrmActivities", "ResponsavelId"),
            ("CrmNotes", "AutorId"),
            ("CrmAttachments", "EnviadoPorId"),
            ("CrmLeadAssignmentHistories", "AlteradoPorId"),
            ("CrmLeadAssignmentHistories", "ResponsavelAnteriorId"),
            ("CrmLeadAssignmentHistories", "ResponsavelNovoId"),
            ("CrmStageHistories", "UsuarioId"),
            ("CrmVeiculos", "VistoriadorId"),
            ("AspNetUsers", "GestorComercialId"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (origem, destino) in Pares)
            {
                // Só junta se as duas contas existirem (banco de desenvolvimento/testes não tem estas).
                var condicao = $"""EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Id" = '{origem}') AND EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Id" = '{destino}')""";

                foreach (var (tabela, coluna) in Colunas)
                {
                    migrationBuilder.Sql($"""
                        UPDATE "{tabela}" SET "{coluna}" = '{destino}'
                        WHERE "{coluna}" = '{origem}' AND {condicao};
                        """);
                }

                // Meta do mês: uma por vendedor/mês — só leva a da provisória se a real não tiver.
                migrationBuilder.Sql($"""
                    UPDATE "CrmSalesGoals" g SET "VendedorId" = '{destino}'
                    WHERE g."VendedorId" = '{origem}' AND {condicao}
                      AND NOT EXISTS (SELECT 1 FROM "CrmSalesGoals" r WHERE r."VendedorId" = '{destino}' AND r."MesReferencia" = g."MesReferencia");
                    """);

                migrationBuilder.Sql($"""
                    UPDATE "AspNetUsers" SET "NomeCompleto" = "NomeCompleto" || ' (conta antiga, unificada)', "Ativo" = false
                    WHERE "Id" = '{origem}' AND "NomeCompleto" NOT LIKE '%(conta antiga, unificada)' AND {condicao};
                    """);
            }

            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" SET "NomeCompleto" = 'Sabrina'
                WHERE "Id" = '01a0ab0d-300e-7391-9599-88575a5e9e51' AND "NomeCompleto" = 'sabrinadinizseguros@gmail.com';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem volta automática: depois da junção não dá para saber quais leads eram da conta provisória.
        }
    }
}
