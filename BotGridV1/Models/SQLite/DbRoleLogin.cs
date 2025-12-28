using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("Roles")]
    public class DbRoleLogin
    {
        [Key]
        [Column("RoleID")]
        public int RoleID { get; set; }

        [Column("RoleCode")]
        [MaxLength(50)]
        [Required]
        public string RoleCode { get; set; } = string.Empty;

        [Column("RoleName")]
        [MaxLength(100)]
        [Required]
        public string RoleName { get; set; } = string.Empty;

        [Column("IsActive")]
        public bool IsActive { get; set; } = true;

        // Navigation property
        public virtual ICollection<DbUserRoleLogin> UserRoles { get; set; } = new List<DbUserRoleLogin>();
    }
}
