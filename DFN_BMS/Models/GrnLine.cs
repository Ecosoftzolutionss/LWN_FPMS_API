using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DFN_BMS.Models
{
    [Table("GRN_LINE")]
    public class GrnLine
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int GrnHeaderId { get; set; }

        public GrnHeader? Header { get; set; }

        [Required]
        public int ItemId { get; set; }

        public ItemMaster? Item { get; set; }

        // GRN Type is maintained at line level so the same Part Number
        // can be entered once as Regular and once as Sample in the same GRN.
        [Required]
        [MaxLength(20)]
        public string GrnType { get; set; } = "Regular";

        [MaxLength(20)]
        public string? Uom { get; set; }

        [Column(TypeName = "decimal(18,3)")]
        public decimal? PalletQuantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Rate { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalValue { get; set; }

        public bool IsPosted { get; set; } = false;

        public string? PostedBy { get; set; }
        public DateTime? PostedDate { get; set; }

        [MaxLength(30)]
        public string? PalletNo { get; set; }

        [MaxLength(30)]
        public string? FifoPalletNo { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
