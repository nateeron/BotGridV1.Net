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
        public DbSet<DbUserAuthori> DbUserAuthoris { get; set; }
        public DbSet<DbRole> DbRoles { get; set; }
        public DbSet<DbUserRole> DbUserRoles { get; set; }
        public DbSet<DbRefreshToken> DbRefreshTokens { get; set; }
        public DbSet<DbUser> DbUsers { get; set; }
        public DbSet<DbRoleLogin> DbRolesLogin { get; set; }
        public DbSet<DbUserRoleLogin> DbUserRolesLogin { get; set; }
        public DbSet<DbUserRefreshTokenLogin> DbUserRefreshTokensLogin { get; set; }
        public DbSet<DbUserLoginLog> DbUserLoginLogs { get; set; }

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

            modelBuilder.Entity<DbUserAuthori>(entity =>
            {
                entity.ToTable("UserAuthori");
                entity.HasKey(e => e.UserID);
                entity.Property(e => e.UserID).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email);
            });

            modelBuilder.Entity<DbRole>(entity =>
            {
                entity.ToTable("db_Roles");
                entity.HasKey(e => e.RoleId);
                entity.Property(e => e.RoleId).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.RoleName).IsUnique();
            });

            modelBuilder.Entity<DbUserRole>(entity =>
            {
                entity.ToTable("db_UserRoles");
                entity.HasKey(e => e.UserRoleId);
                entity.Property(e => e.UserRoleId).ValueGeneratedOnAdd();
                entity.HasIndex(e => new { e.UserId, e.RoleId });
            });

            modelBuilder.Entity<DbRefreshToken>(entity =>
            {
                entity.ToTable("RefreshTokens");
                entity.HasKey(e => e.RefreshTokenId);
                entity.Property(e => e.RefreshTokenId).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.UserId);
                entity.HasIndex(e => e.Token);
            });

            modelBuilder.Entity<DbUser>(entity =>
            {
                entity.ToTable("Users");
                entity.HasKey(e => e.UserID);
                entity.Property(e => e.UserID).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.Username).IsUnique();
                entity.HasIndex(e => e.Email);
            });

            modelBuilder.Entity<DbRoleLogin>(entity =>
            {
                entity.ToTable("Roles");
                entity.HasKey(e => e.RoleID);
                entity.Property(e => e.RoleID).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.RoleCode).IsUnique();
            });

            modelBuilder.Entity<DbUserRoleLogin>(entity =>
            {
                entity.ToTable("UserRoles");
                entity.HasKey(e => e.UserRoleID);
                entity.Property(e => e.UserRoleID).ValueGeneratedOnAdd();
                entity.HasIndex(e => new { e.UserID, e.RoleID }).IsUnique();
            });

            modelBuilder.Entity<DbUserRefreshTokenLogin>(entity =>
            {
                entity.ToTable("UserRefreshTokens");
                entity.HasKey(e => e.TokenID);
                entity.Property(e => e.TokenID).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.UserID);
                entity.HasIndex(e => e.RefreshToken);
            });

            modelBuilder.Entity<DbUserLoginLog>(entity =>
            {
                entity.ToTable("UserLoginLogs");
                entity.HasKey(e => e.LogID);
                entity.Property(e => e.LogID).ValueGeneratedOnAdd();
                entity.HasIndex(e => e.UserID);
                entity.HasIndex(e => e.LoginAt);
            });
        }
    }
}

