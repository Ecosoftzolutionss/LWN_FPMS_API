using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DFN_BMS.Models
{
    [Table("SUPPLIER_GROUP_MASTER")]
    public class SupplierGroupMaster
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string? SupplierGroupType { get; set; }

        [MaxLength(250)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }
    }
}