using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    // A stuffable position (e.g. "P1", "P2") within a Store Master
    // location, each with a fixed capacity. Lightweight and separate
    // from the Rack/Column/Row hierarchy in Location Master — this
    // screen deals in simple per-store pallet slots, not the full
    // rack structure.
    [Table("STORE_POSITION")]
    public class StorePosition
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int StoreMasterId { get; set; }   // FK -> StoreMaster.Id

     
        public StoreMaster? Store { get; set; }

        [Required]
        [MaxLength(10)]
        public string PositionCode { get; set; }   // e.g. "P1", "P2"

        [Required]
        [Column(TypeName = "decimal(18,3)")]
        public decimal Capacity { get; set; } = 5000;

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}