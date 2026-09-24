using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DFN_BMS.Models
{
    [Table("CUSTOMER_GROUP_MASTER")]
    public class CustomerGroupMaster
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string? CustomerGroupType { get; set; }

        [MaxLength(250)]
        public string? Description { get; set; }

        // True only when an External customer group requires GST details.
        public bool HasGST { get; set; } = false;

        public bool? IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }
    }
}
