using Microsoft.EntityFrameworkCore;
using PhotoApp.Models;

namespace PhotoApp.Data
{
    // DbContext s explicitním mapováním PhotoRecord -> Photos a základnou konfigurací sloupcù.
    // To pomùže zajistit, že EF bude oèekávat správné názvy tabulek/sloupcù a že migrace budou jasnìjší.
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        public DbSet<CustomUser> Users { get; set; } = default!;
        public DbSet<PhotoRecord> Photos { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Explicitnì mapujeme PhotoRecord na tabulku "Photos" (pokud má DB jiný název, zmìòte zde)
            modelBuilder.Entity<PhotoRecord>(entity =>
            {
                entity.ToTable("Photos");

                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                      .IsRequired()
                      .HasMaxLength(250);

                entity.Property(e => e.Code)
                      .HasMaxLength(100);

                entity.Property(e => e.Type)
                      .HasMaxLength(100);

                entity.Property(e => e.Supplier)
                      .HasMaxLength(200);

                entity.Property(e => e.OriginalName)
                      .HasMaxLength(500);

                entity.Property(e => e.Material)
                      .HasMaxLength(150);

                entity.Property(e => e.Form)
                      .HasMaxLength(150);

                entity.Property(e => e.Filler)
                      .HasMaxLength(150);

                entity.Property(e => e.Color)
                      .HasMaxLength(150);

                entity.Property(e => e.Description)
                      .HasMaxLength(2000);

                entity.Property(e => e.MonthlyQuantity)
                      .HasMaxLength(200);

                entity.Property(e => e.Mfi)
                      .HasMaxLength(100);

                entity.Property(e => e.Notes)
                      .HasMaxLength(2000);

                entity.Property(e => e.PhotoFileName)
                      .HasMaxLength(500);

                entity.Property(e => e.PhotoPath)
                      .HasMaxLength(500);

                entity.Property(e => e.ImagePath)
                      .HasMaxLength(500);

                entity.Property(e => e.Position)
                      .HasMaxLength(200);

                entity.Property(e => e.ExternalId)
                      .HasMaxLength(200);

                // Doporuèené výchozí hodnoty pro èasová pole v SQLite
                entity.Property(e => e.CreatedAt)
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.UpdatedAt)
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // Pokud používáte vlastní CustomUser (Identity), zajistìte mapování (pokud chcete jiný název tabulky).
            // Pokud používáte standardní ASP.NET Identity, nechte to v defaultu nebo mapujte na "AspNetUsers".
            modelBuilder.Entity<CustomUser>(entity =>
            {
                // odkomentujte a upravte podle potøeby:
                // entity.ToTable("AspNetUsers");
            });
        }
    }
}