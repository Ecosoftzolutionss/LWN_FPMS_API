using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    // Top-level rack container within a store, e.g. "A", "B".
    [Table("LOCATION_RACK")]
    public class LocationRack
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StoreId { get; set; }   // FK -> LocationMaster.Id

    
        public LocationMaster? Store { get; set; }

        [Required]
        [MaxLength(5)]
        public string RackNo { get; set; }   // e.g. "A", "B"

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public List<RackColumn> Columns { get; set; } = new List<RackColumn>();
    }
}