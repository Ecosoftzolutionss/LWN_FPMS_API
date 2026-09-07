using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DFN_BMS.Models
{
    [Table("ITEM_MASTER")]
    public class ItemMaster
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        public string ItemNumber { get; set; }

        [Required]
        [MaxLength(150)]
        public string ItemName { get; set; }

        [Required]
        public int ItemGroupId { get; set; }

        public ItemGroupMaster? ItemGroup { get; set; }

        [MaxLength(20)]
        public string HsnCode { get; set; }

        // Commercial Information
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal UnitPrice { get; set; }

        [Required]
        [MaxLength(20)]
        public string CustomerOrSupplier { get; set; }

        [Required]
        public DateTime EffectiveDate { get; set; }

        [Required]
        [MaxLength(20)]
        public string Uom { get; set; }

        // Item Physical Information
        [Column(TypeName = "decimal(18,3)")]
        public decimal? WeightPerUnit { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal? StuffQuantity { get; set; }

        [MaxLength(100)]
        public string ItemModel { get; set; }

        [MaxLength(30)]
        public string Usage { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Length { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Width { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal? Height { get; set; }

        [MaxLength(500)]
        public string Description { get; set; }

        // Stock Level Information
        [Required]
        [Column(TypeName = "decimal(18,3)")]
        public decimal SafetyLevel { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,3)")]
        public decimal ReorderLevel { get; set; }

        [Required]
        [MaxLength(20)]
        public string DangerLevel { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }
    }
}
