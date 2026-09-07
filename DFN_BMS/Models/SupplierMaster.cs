using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DFN_BMS.Models
{
    [Table("SUPPLIER_MASTER")]
    public class SupplierMaster
    {
        [Key]
        public int Id { get; set; }

        // ---------------- SUPPLIER BASIC INFORMATION ----------------

        [Required]
        [MaxLength(30)]
        public string SupplierCode { get; set; } = string.Empty;
        // Manually entered Supplier ID

        [Required]
        [MaxLength(150)]
        public string SupplierName { get; set; } = string.Empty;

        [Required]
        public int SupplierGroupId { get; set; }

        // Navigation property
        [ForeignKey(nameof(SupplierGroupId))]
        public SupplierGroupMaster? SupplierGroup { get; set; }

        [Required]
        [MaxLength(100)]
        [EmailAddress(ErrorMessage = "Enter a valid email address")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        [RegularExpression(
            @"^[0-9]{10}$",
            ErrorMessage = "Contact Number must be exactly 10 digits")]
        public string ContactNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string PersonToContact { get; set; } = string.Empty;

        [Required]
        [MaxLength(15)]
        [RegularExpression(
            @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$",
            ErrorMessage = "Enter a valid 15-character GSTIN (e.g. 33ABCDE1234F1Z5)")]
        public string GstNo { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        [RegularExpression(
            @"^[A-Z]{5}[0-9]{4}[A-Z]{1}$",
            ErrorMessage = "Enter a valid 10-character PAN (e.g. ABCDE1234F)")]
        public string PanNo { get; set; } = string.Empty;

        // ---------------- AUDIT INFORMATION ----------------

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }
    }
}