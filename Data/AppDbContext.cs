using Microsoft.EntityFrameworkCore;
using PhotoApp.Models;

namespace PhotoApp.Data
{
    public class AppDbContext : DbContext
    {
        public DbSet<PhotoRecord> Photos => Set<PhotoRecord>();

        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    }
}
