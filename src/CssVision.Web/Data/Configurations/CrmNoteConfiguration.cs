using CssVision.Web.Domain.Crm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CssVision.Web.Data.Configurations;

public class CrmNoteConfiguration : IEntityTypeConfiguration<CrmNote>
{
    public void Configure(EntityTypeBuilder<CrmNote> builder)
    {
        builder.ConfigureCrmBase();
        builder.ToTable("CrmNotes");

        builder.Property(e => e.Texto).IsRequired().HasMaxLength(4000);
        builder.HasIndex(e => e.LeadId);

        builder.HasOne(e => e.Lead)
            .WithMany(l => l.Notas)
            .HasForeignKey(e => e.LeadId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(e => e.Autor)
            .WithMany()
            .HasForeignKey(e => e.AutorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
