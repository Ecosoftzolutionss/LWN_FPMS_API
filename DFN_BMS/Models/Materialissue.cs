using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    [Table("MATERIAL_ISSUE")]
    public class MaterialIssue
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        [ValidateNever]
        public string IssueNumber { get; set; }   // auto-generated, e.g. MI-2026-0001

        [Required]
        public int ItemId { get; set; }   // FK -> ItemMaster.Id (Part Number)

    
        public ItemMaster? Item { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        [Required]
        [MaxLength(100)]
        public string IssuedTo { get; set; }      // department / person receiving the material

        [Required]
        [MaxLength(100)]
        public string IssuedBy { get; set; }      // stores person issuing it

        [MaxLength(100)]
        public string? StoreLocation { get; set; }

        [MaxLength(30)]
        public string? PalletNo { get; set; }     // which pallet this came from, if known

        [MaxLength(30)]
        public string? GrnNumber { get; set; }    // GRN this pallet came from, from the scanned QR code

        [MaxLength(250)]
        public string? Remarks { get; set; }

        public DateTime IssueDate { get; set; } = DateTime.Now;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public int? GrnPalletId { get; set; }
    }
}