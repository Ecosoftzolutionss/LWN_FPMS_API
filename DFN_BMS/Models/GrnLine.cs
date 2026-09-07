using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    [Table("GRN_LINE")]
    public class GrnLine
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int GrnHeaderId { get; set; }   // FK -> GrnHeader.Id

     
        public GrnHeader? Header { get; set; }

        [Required]
        public int ItemId { get; set; }   // FK -> ItemMaster.Id (Part Number)

   
        public ItemMaster? Item { get; set; }

        [MaxLength(20)]
        public string Uom { get; set; }

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

        //test

        // ---------- Per-line posting / FIFO label fields ----------
        public bool IsPosted { get; set; } = false;

        public string? PostedBy { get; set; }

        public DateTime? PostedDate { get; set; }

        [MaxLength(30)]
        public string? PalletNo { get; set; }        // e.g. EX-09, assigned on Post

        [MaxLength(30)]
        public string? FifoPalletNo { get; set; }     // e.g. F25070001, assigned on Post

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}