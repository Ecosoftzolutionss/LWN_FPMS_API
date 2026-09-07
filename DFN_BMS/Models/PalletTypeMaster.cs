using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DFN_BMS.Models
{
    [Table("PALLET_TYPE_MASTER")]
    public class PalletTypeMaster
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(10)]
        public string PalletName { get; set; }

        [Required]
        public int RangeFrom { get; set; } = 1;

        [Required]
        public int RangeTo { get; set; }

        [Required]
        public int CurrentSequence { get; set; } = 0;

        [MaxLength(7)]
        [RegularExpression(
            @"^#[0-9A-Fa-f]{6}$",
            ErrorMessage = "Colour must be a valid hex code"
        )]
        public string? ColourCode { get; set; }
    }
}