using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("db_UserRoles")]
    public class DbUserRole
    {
        [Key]
        [Column("UserRoleId")]
        public int UserRoleId { get; set; }

        [Column("UserId")]
        [Required]
        public int UserId { get; set; }

        [Column("RoleId")]
        [Required]
        public int RoleId { get; set; }

        // Navigation properties
        [ForeignKey("UserId")]
        public virtual DbUserAuthori? User { get; set; }

        [ForeignKey("RoleId")]
        public virtual DbRole? Role { get; set; }
    }
}
