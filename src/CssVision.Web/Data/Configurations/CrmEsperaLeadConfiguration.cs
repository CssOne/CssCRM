using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmEsperaLeadConfiguration : IEntityTypeConfiguration<CrmEsperaLead>
{
    public void Configure(EntityTypeBuilder<CrmEsperaLead> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmEsperasLead");
        builder.HasIndex(e => e.LeadId).IsUnique();
        // O aviso à gestão conta, por consultor, os leads que chegaram ao segundo aviso nos últimos dias.
        builder.HasIndex(e => new { e.ConsultorId, e.AvisoUrgenteEm }).HasFilter("\"AvisoUrgenteEm\" IS NOT NULL");
        builder.HasOne(e => e.Lead).WithMany().HasForeignKey(e => e.LeadId).OnDelete(DeleteBehavior.Cascade);
    }
}
