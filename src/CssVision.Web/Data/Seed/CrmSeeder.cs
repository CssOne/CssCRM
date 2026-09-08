using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;

namespace CssVision.Web.Data.Seed;

/// <summary>Semeia as oito etapas iniciais do funil e alguns motivos de perda comuns.</summary>
public static class CrmSeeder
{
    public static async Task SeedAsync(ApplicationDbContext db)
    {
        if (!await db.CrmPipelineStages.AnyAsync())
        {
            db.CrmPipelineStages.AddRange(
                new CrmPipelineStage { Nome = "Novo lead", Ordem = 1, Tipo = TipoEtapaPipeline.Aberta, Cor = "#64748b" },
                new CrmPipelineStage { Nome = "Tentativa de contato", Ordem = 2, Tipo = TipoEtapaPipeline.Aberta, Cor = "#3b82f6" },
                new CrmPipelineStage { Nome = "Contato realizado", Ordem = 3, Tipo = TipoEtapaPipeline.Aberta, Cor = "#0ea5e9" },
                new CrmPipelineStage { Nome = "Qualificado", Ordem = 4, Tipo = TipoEtapaPipeline.Aberta, Cor = "#8b5cf6" },
                new CrmPipelineStage { Nome = "Proposta enviada", Ordem = 5, Tipo = TipoEtapaPipeline.Aberta, Cor = "#f59e0b" },
                new CrmPipelineStage { Nome = "Negociação", Ordem = 6, Tipo = TipoEtapaPipeline.Aberta, Cor = "#f97316" },
                new CrmPipelineStage { Nome = "Ganho", Ordem = 7, Tipo = TipoEtapaPipeline.Ganho, Cor = "#22c55e" },
                new CrmPipelineStage { Nome = "Perdido", Ordem = 8, Tipo = TipoEtapaPipeline.Perdido, Cor = "#ef4444" }
            );
        }

        if (!await db.CrmLossReasons.AnyAsync())
        {
            db.CrmLossReasons.AddRange(
                new CrmLossReason { Descricao = "Preço" },
                new CrmLossReason { Descricao = "Concorrência" },
                new CrmLossReason { Descricao = "Sem orçamento" },
                new CrmLossReason { Descricao = "Sem resposta do cliente" },
                new CrmLossReason { Descricao = "Fora do perfil" }
            );
        }

        await db.SaveChangesAsync();
    }
}
