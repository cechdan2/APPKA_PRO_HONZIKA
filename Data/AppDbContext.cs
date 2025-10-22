using Microsoft.EntityFrameworkCore;
using PhotoApp.Models;

namespace PhotoApp.Data
{
    // P�epnuto na DbContext a vlastn� Users DbSet
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options) { }

        public DbSet<CustomUser> Users { get; set; } = default!;
        public DbSet<PhotoRecord> Photos { get; set; } = default!;
    }
}