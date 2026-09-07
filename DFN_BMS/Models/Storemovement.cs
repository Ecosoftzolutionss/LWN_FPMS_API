using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    // A stuffing/occupancy record. Targets EITHER:
    //  - StorePositionId (the simplified P1/P2 model used by the
    //    Store Movement transaction screen), tied to a GrnPalletId, OR
    //  - RackRowId + SlotNumber (a specific slot in Location Master's
    //    Rack -> Column -> Row structure), used for quick manual
    //    occupy/vacate from the rack preview, with no GrnPallet tie.
    // Exactly one of the two targets should be set — enforced in the
    // controllers, not the database.
    [Table("STORE_MOVEMENT")]
    public class StoreMovement
    {
        [Key]
        public int Id { get; set; }

        public int? GrnPalletId { get; set; }

      
        public GrnPallet? GrnPallet { get; set; }

        public int? StorePositionId { get; set; }

      
        public StorePosition? StorePosition { get; set; }

        public int? RackRowId { get; set; }

     
        public RackRow? RackRow { get; set; }

        public int? SlotNumber { get; set; }

        [MaxLength(100)]
        public string? Note { get; set; }   // free-text reference for quick rack-slot occupancy

        [Required]
        [MaxLength(10)]
        public string Side { get; set; } = "Front";   // "Front" or "Rear"

        [Required]
        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        public DateTime MovementDate { get; set; } = DateTime.Now;

        public string? CreatedBy { get; set; }
    }
}