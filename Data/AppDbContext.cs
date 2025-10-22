using Microsoft.EntityFrameworkCore;
using PhotoApp.Models;

namespace PhotoApp.Data
{
    // DbContext s explicitn�m mapov�n�m PhotoRecord -> Photos a z�kladnou konfigurac� sloupc�.
    // To pom��e zajistit, �e EF bude o�ek�vat spr�vn� n�zvy tabulek/sloupc� a �e migrace budou jasn�j��.
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        public DbSet<CustomUser> Users { get; set; } = default!;
        public DbSet<PhotoRecord> Photos { get; set; } = default!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Explicitn� mapujeme PhotoRecord na tabulku "Photos" (pokud m� DB jin� n�zev, zm��te zde)
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

                // Doporu�en� v�choz� hodnoty pro �asov� pole v SQLite
                entity.Property(e => e.CreatedAt)
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");

                entity.Property(e => e.UpdatedAt)
                      .HasDefaultValueSql("CURRENT_TIMESTAMP");
            });

            // Pokud pou��v�te vlastn� CustomUser (Identity), zajist�te mapov�n� (pokud chcete jin� n�zev tabulky).
            // Pokud pou��v�te standardn� ASP.NET Identity, nechte to v defaultu nebo mapujte na "AspNetUsers".
            modelBuilder.Entity<CustomUser>(entity =>
            {
                // odkomentujte a upravte podle pot�eby:
                // entity.ToTable("AspNetUsers");
            });
        }
    }
}