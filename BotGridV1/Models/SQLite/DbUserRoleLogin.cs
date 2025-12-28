using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("UserRoles")]
    public class DbUserRoleLogin
    {
        [Key]
        [Column("UserRoleID")]
        public int UserRoleID { get; set; }

        [Column("UserID")]
        [Required]
        public int UserID { get; set; }

        [Column("RoleID")]
        [Required]
        public int RoleID { get; set; }

        // Navigation properties
        [ForeignKey("UserID")]
        public virtual DbUser? User { get; set; }

        [ForeignKey("RoleID")]
        public virtual DbRoleLogin? Role { get; set; }
    }
}
