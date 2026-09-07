using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    // A row within a Column, e.g. "R1", "R2" — holds the actual
    // Front/Rear + Fixture (slot-pair count) data.
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

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}