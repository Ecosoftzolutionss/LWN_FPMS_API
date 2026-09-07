using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    [Table("LOCATION_MASTER")]
    public class LocationMaster
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(30)]
        [ValidateNever]
        public string StoreCode { get; set; }   // auto-generated, e.g. ST000001

        [Required]
        public int StoreMasterId { get; set; }   // FK -> StoreMaster.Id

    
        public StoreMaster? StoreMaster { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }

        public List<LocationRack> Racks { get; set; } = new List<LocationRack>();
    }
}