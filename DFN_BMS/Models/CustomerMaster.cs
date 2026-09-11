using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DFN_BMS.Models
{
    [Table("CUSTOMER_MASTER")]
    public class CustomerMaster
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        public string CustomerCode { get; set; }

        [Required]
        [MaxLength(150)]
        [RegularExpression(
            @"^[A-Za-z0-9_ ]+$",
            ErrorMessage = "Only letters, numbers, underscore and spaces are allowed (e.g. Test_233)"
        )]
        public string CustomerName { get; set; }

        [Required]
        [MaxLength(100)]
        public string CustomerDivision { get; set; }

        // Customer Group Foreign Key
        [Required]
        public int CustomerGroupId { get; set; }

        // Navigation Property
        [ForeignKey(nameof(CustomerGroupId))]
        public CustomerGroupMaster? CustomerGroup { get; set; }

        [Required]
        [MaxLength(20)]
        public string MobileNumber { get; set; }

        [Required]
        [MaxLength(100)]
        public string EmailId { get; set; }

        // ---------------------------------------------------------
        // GST No
        //
        // External Customer -> Actual GSTIN
        // Internal Customer -> NOTPROVIDED
        // ---------------------------------------------------------
        [MaxLength(20)]
        [RegularExpression(
            @"^([0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}|NOTPROVIDED)$",
            ErrorMessage = "Enter a valid 15-character GSTIN (e.g. 33ABCDE1234F1Z5)"
        )]
        public string? GstNo { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }
    }
}