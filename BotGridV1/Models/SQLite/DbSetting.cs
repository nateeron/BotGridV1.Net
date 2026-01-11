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

    public class req_GetOrdersByPage
    {
        public int page { get; set; } = 1;
        public int pageSize { get; set; } = 50;
        public string filter { get; set; } = "All"; // All, WAITING_SELL, SOLD
    }

    public class req_GetProfitLossReport
    {
        public DateTime? DateFrom { get; set; } // Optional: if null or empty, select all dates
        public DateTime? DateTo { get; set; } // Optional: if null or empty, select all dates
        public string Period { get; set; } //= ReportPeriod.Day;
        public int? SettingId { get; set; } // Optional: filter by Setting ID
        public int? PeriodCount { get; set; } // Optional: limit number of periods to return
    }


    public class ProfitLossReport
    {
        public string Period { get; set; }
        public decimal TotalProfit { get; set; }
    }
    public enum ReportPeriod
    {
        Hour,
        HalfDay,
        Day,
        Week,
        Month,
        Year
    }

}

