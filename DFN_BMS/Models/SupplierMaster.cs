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

        [Required]
        [MaxLength(30)]
        public string SupplierCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string SupplierName { get; set; } = string.Empty;

        [Required]
        public int SupplierGroupId { get; set; }

        [ForeignKey(nameof(SupplierGroupId))]
        public SupplierGroupMaster? SupplierGroup { get; set; }

        [MaxLength(100)]
        [EmailAddress(ErrorMessage = "Enter a valid email address")]
        public string? Email { get; set; }

        [MaxLength(10)]
        [RegularExpression(@"^[0-9]{10}$", ErrorMessage = "Contact Number must be exactly 10 digits")]
        public string? ContactNumber { get; set; }

        [MaxLength(100)]
        public string? PersonToContact { get; set; }

        // These are conditionally required based on Supplier Group Master.
        // If RequiresGst = false, GST can be NULL.
        [MaxLength(15)]
        public string? GstNo { get; set; }

        // If RequiresPan = false, PAN can be NULL.
        [MaxLength(10)]
        public string? PanNo { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }
    }
}
