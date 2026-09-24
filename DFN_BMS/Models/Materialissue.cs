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
        public string? IssueNumber { get; set; }   

        [Required]
        public int ItemId { get; set; }   
        public ItemMaster? Item { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        [Required]
        [MaxLength(100)]
        public string IssuedTo { get; set; }      

        [Required]
        [MaxLength(100)]
        public string IssuedBy { get; set; }      

        [MaxLength(100)]
        public string? StoreLocation { get; set; }

        [MaxLength(30)]
        public string? PalletNo { get; set; }    

        [MaxLength(30)]
        public string? GrnNumber { get; set; }    

        [MaxLength(250)]
        public string? Remarks { get; set; }

        public DateTime IssueDate { get; set; } = DateTime.Now;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public int? GrnPalletId { get; set; }

        public string? IdempotencyKey { get; set; }
        public string? DeviceId { get; set; }
    }
}