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
        [MaxLength(50)]
        public string CustomerCode { get; set; } = string.Empty;

        [Required]
        [MaxLength(150)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string CustomerDivision { get; set; } = string.Empty;

        [Required]
        public int CustomerGroupId { get; set; }

        [ForeignKey(nameof(CustomerGroupId))]
        public CustomerGroupMaster? CustomerGroup { get; set; }

        [MaxLength(20)]
        public string? MobileNumber { get; set; }

        // Optional Email ID
        [MaxLength(100)]
        public string? EmailId { get; set; }

        [MaxLength(20)]
        public string? GstNo { get; set; }

        [NotMapped]
        public bool GstAvailable { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }
    }
}
