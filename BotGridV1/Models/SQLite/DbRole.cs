using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("db_Roles")]
    public class DbRole
    {
        [Key]
        [Column("RoleId")]
        public int RoleId { get; set; }

        [Column("RoleName")]
        [MaxLength(100)]
        [Required]
        public string RoleName { get; set; } = string.Empty;

        // Navigation property
        public virtual ICollection<DbUserRole> UserRoles { get; set; } = new List<DbUserRole>();
    }
}
