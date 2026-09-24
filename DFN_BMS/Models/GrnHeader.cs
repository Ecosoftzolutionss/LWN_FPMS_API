using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    [Table("GRN_HEADER")]
    public class GrnHeader
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        [ValidateNever]
        public string? GrnNumber { get; set; }

        [Required]
        public int SupplierId { get; set; }

        public SupplierMaster? Supplier { get; set; }

        [Required]
        [MaxLength(30)]
        public string? PoNumber { get; set; }

        [Required]
        public DateTime PoDate { get; set; }

        // Customer-maintained GRN Date.
        [Required]
        public DateTime GrnDate { get; set; }

        // Regular / Sample / Mixed.
        // Mixed is stored when the same GRN contains both Regular and Sample lines.
        [Required]
        [MaxLength(20)]
        public string? GrnType { get; set; }

        [Required]
        [MaxLength(30)]
        public string SupplierInvoiceNumber { get; set; }

        [Required]
        public DateTime SupplierInvoiceDate { get; set; }

        public bool IsPosted { get; set; } = false;
        public DateTime? PostedDate { get; set; }

        [MaxLength(30)]
        public string? PalletNo { get; set; }

        public string? CreatedBy { get; set; }

        [MaxLength(30)]
        public string? FifoPalletNo { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public List<GrnLine> Lines { get; set; } = new List<GrnLine>();
    }
}
