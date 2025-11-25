using Microsoft.EntityFrameworkCore;

namespace BotGridV1.Models.SQLite
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<DbSetting> DbSettings { get; set; }
        public DbSet<DbOrder> DbOrders { get; set; }
        public DbSet<DbAlert> DbAlerts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<DbSetting>(entity =>
            {
                entity.ToTable("db_setting");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
            });

            modelBuilder.Entity<DbOrder>(entity =>
            {
                entity.ToTable("db_Order");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => new { e.Status, e.PriceWaitSell });
            });

            modelBuilder.Entity<DbAlert>(entity =>
            {
                entity.ToTable("db_alert");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Id).ValueGeneratedOnAdd();
                entity.HasIndex(e => new { e.Timestamp, e.IsRead });
                entity.HasIndex(e => e.ConfigId);
            });
        }
    }
}

