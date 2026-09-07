using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    // A column within a Rack, e.g. "A1", "B1".
    [Table("RACK_COLUMN")]
    public class RackColumn
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int LocationRackId { get; set; }   // FK -> LocationRack.Id

    
        public LocationRack? Rack { get; set; }

        [Required]
        [MaxLength(10)]
        public string ColumnNo { get; set; }   // e.g. "A1", "B1"

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public List<RackRow> Rows { get; set; } = new List<RackRow>();
    }
}