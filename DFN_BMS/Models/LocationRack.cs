using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    [Table("LOCATION_RACK")]
    public class LocationRack
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StoreId { get; set; }
        public LocationMaster? Store { get; set; }
        [Required]
        [MaxLength(5)]
        public string RackNo { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
        public List<RackColumn> Columns { get; set; } = new List<RackColumn>();
    }
}
