using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    // A physical pallet derived from one GRN line. Simplification: one
    // pallet per line (PalletNo assigned sequentially across the whole
    // GRN, e.g. P001, P002...). If your warehouse actually splits a
    // single line's quantity across multiple standard-size pallets,
    // this needs a PalletSize/split step added before creation.
    [Table("GRN_PALLET")]
    public class GrnPallet
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int GrnLineId { get; set; }   // FK -> GrnLine.Id

     
        public GrnLine? GrnLine { get; set; }

        [Required]
        [MaxLength(20)]
        public string PalletNo { get; set; }   // e.g. P001, P002

        [Required]
        [Column(TypeName = "decimal(18,3)")]
        public decimal Quantity { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Rate { get; set; }
        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}