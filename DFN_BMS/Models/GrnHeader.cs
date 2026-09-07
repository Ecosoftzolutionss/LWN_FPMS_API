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
        public string GrnNumber { get; set; }   // auto-generated, e.g. GRN-2026-0001

        [Required]
        public int SupplierId { get; set; }   // FK -> SupplierMaster.Id

    
        public SupplierMaster? Supplier { get; set; }

        [Required]
        [MaxLength(30)]
        public string PoNumber { get; set; }

        [Required]
        public DateTime PoDate { get; set; }

        [Required]
        [MaxLength(20)]
        public string GrnType { get; set; }   // "Regular" or "Sample"

        [Required]
        [MaxLength(30)]
        public string SupplierInvoiceNumber { get; set; }

        [Required]
        public DateTime SupplierInvoiceDate { get; set; }

        // ---------- Post / FIFO label fields ----------
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