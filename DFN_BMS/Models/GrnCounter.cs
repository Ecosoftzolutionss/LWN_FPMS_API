using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DFN_BMS.Models
{
    // A single active counter row. Seeded the first time a GRN No is
    // manually entered — the numeric suffix of that entry becomes the
    // starting sequence, and every GRN after that just increments it,
    // keeping the same text Prefix and digit-width (PadWidth).
    // e.g. first manual entry "260001" -> Prefix "26", PadWidth 4,
    // LastSequence 1 -> next auto value is "260002".GRN_COUNTER
    [Table("GRN_COUNTER")]
    public class GrnCounter
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string? Prefix { get; set; }

        [Required]
        public int   PadWidth { get; set; }

        [Required]
        public int  LastSequence { get; set; }
    }
}