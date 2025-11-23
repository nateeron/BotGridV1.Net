using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BotGridV1.Models.SQLite
{
    [Table("db_Order")]
    public class DbOrder
    {
        [Key]
        [Column("id")]
        public int Id { get; set; }

        [Column("timestamp")]
        public DateTime Timestamp { get; set; }

        [Column("OrderBuyID")]
        [MaxLength(100)]
        public string? OrderBuyID { get; set; }

        [Column("PriceBuy", TypeName = "decimal(18,8)")]
        public decimal? PriceBuy { get; set; }

        [Column("PriceWaitSell", TypeName = "decimal(18,8)")]
        public decimal? PriceWaitSell { get; set; }

        [Column("OrderSellID")]
        [MaxLength(100)]
        public string? OrderSellID { get; set; }

        [Column("PriceSellActual", TypeName = "decimal(18,8)")]
        public decimal? PriceSellActual { get; set; }

        [Column("ProfitLoss", TypeName = "decimal(18,8)")]
        public decimal? ProfitLoss { get; set; }

        [Column("DateBuy")]
        public DateTime? DateBuy { get; set; }

        [Column("DateSell")]
        public DateTime? DateSell { get; set; }

        [Column("setting_ID")]
        public int Setting_ID { get; set; }

        [Column("Status")]
        [MaxLength(50)]
        public string Status { get; set; } = "WAITING_BUY"; // WAITING_BUY, BOUGHT, WAITING_SELL, SOLD

        [Column("Symbol")]
        [MaxLength(100)]
        public string? Symbol { get; set; }

        [Column("Quantity", TypeName = "decimal(18,8)")]
        public decimal? Quantity { get; set; }

        [Column("BuyAmountUSD", TypeName = "decimal(18,2)")]
        public decimal? BuyAmountUSD { get; set; } // จำนวนเงินซื้อขาย (USD)

        [Column("CoinQuantity", TypeName = "decimal(18,8)")]
        public decimal? CoinQuantity { get; set; } // จำนวนCoinSell - จำนวน Coin ที่ซื้อมา
    }
}

