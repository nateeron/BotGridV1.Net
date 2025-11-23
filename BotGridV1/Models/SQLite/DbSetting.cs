using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("db_setting")]
    public class DbSetting
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("Config_Version")]
        public int Config_Version { get; set; }

        [Column("API_KEY")]
        [MaxLength(500)]
        public string? API_KEY { get; set; }

        [Column("API_SECRET")]
        [MaxLength(500)]
        public string? API_SECRET { get; set; }

        [Column("DisCord_Hook1")]
        [MaxLength(500)]
        public string? DisCord_Hook1 { get; set; }

        [Column("DisCord_Hook2")]
        [MaxLength(500)]
        public string? DisCord_Hook2 { get; set; }

        [Column("SYMBOL")]
        [MaxLength(100)]
        public string? SYMBOL { get; set; }

        [Column("PERCEN_BUY", TypeName = "decimal(18,2)")]
        public decimal PERCEN_BUY { get; set; }

        [Column("PERCEN_SELL", TypeName = "decimal(18,2)")]
        public decimal PERCEN_SELL { get; set; }

        [Column("BuyAmountUSD", TypeName = "decimal(18,2)")]
        public decimal? BuyAmountUSD { get; set; } // จำนวนเงินซื้อขาย (USD)
    }
    public class req_GetById
    {
        public int id { get; set; }
    }

    public class req_GetOrdersByStatus
    {
        public string? Status { get; set; }
        public int? SettingId { get; set; }
    }

    public class req_DeleteOrders
    {
        public List<int> Ids { get; set; } = new List<int>();
    }

    public class req_DeleteOrdersByStatus
    {
        public string Status { get; set; } = string.Empty;
        public int? SettingId { get; set; }
    }
}

