using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    [Table("RACK_ROW")]
    public class RackRow
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int RackColumnId { get; set; }

        public RackColumn? Column { get; set; }

        [Required]
        [MaxLength(10)]
        public string RowNo { get; set; }

        public bool HasFront { get; set; } = true;

        public bool HasRear { get; set; } = true;

        [Required]
        public int Fixture { get; set; } = 1;

        // LTR = Left to Right, RTL = Right to Left.
        // This controls the pallet/slot numbering sequence for this level.
        [Required]
        [MaxLength(3)]
        public string StorageDirection { get; set; } = "LTR";

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
