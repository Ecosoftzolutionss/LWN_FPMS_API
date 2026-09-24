using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    [Table("RACK_COLUMN")]
    public class RackColumn
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int LocationRackId { get; set; }

        public LocationRack? Rack { get; set; }

        [Required]
        [MaxLength(10)]
        public string ColumnNo { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public List<RackRow> Rows { get; set; } = new List<RackRow>();
    }
}
