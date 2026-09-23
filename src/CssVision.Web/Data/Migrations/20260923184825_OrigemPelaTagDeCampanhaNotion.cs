using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CssVision.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class OrigemPelaTagDeCampanhaNotion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Leads do Notion (migrados ou sincronizados) passam a ter como Origem a tag de campanha do
            // card (Lookalike, UGC, Pmax...) — é o que o gráfico "Leads por origem" do Tráfego Pago
            // mostra. Sem tag reconhecida, a Origem técnica ("Migração Notion"/"Sincronização Notion")
            // fica como está. Os leads migrados continuam identificáveis pelo ConsentimentoOrigem.
            migrationBuilder.Sql("""
                UPDATE "CrmLeads" SET "Origem" = CASE lower(btrim("Campanha"))
                    WHEN 'lookalike' THEN 'Lookalike'
                    WHEN 'ugc venda' THEN 'UGC VENDA'
                    WHEN 'ugc caminhão' THEN 'UGC CAMINHÃO'
                    WHEN 'pesquisa' THEN 'Pesquisa'
                    WHEN 'pmax' THEN 'Pmax'
                    WHEN 'demand gen' THEN 'Demand Gen'
                    WHEN 'ugc' THEN 'UGC'
                    WHEN 'influencers' THEN 'INFLUENCERS'
                    WHEN 'lead convertido' THEN 'Lead convertido'
                    WHEN 'caixa de pergunta' THEN 'Caixa de pergunta'
                    END
                WHERE "ConsentimentoOrigem" IN ('Migração da base histórica (Notion)', 'Sincronização automática (Notion)')
                  AND lower(btrim("Campanha")) IN ('lookalike', 'ugc venda', 'ugc caminhão', 'pesquisa', 'pmax', 'demand gen', 'ugc', 'influencers', 'lead convertido', 'caixa de pergunta');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
