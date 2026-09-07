using DFN_BMS.Models;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class StoreVerification
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int ItemId { get; set; }

    public int? PalletId { get; set; }


    public ItemMaster? Item { get; set; }

    [MaxLength(100)]
    public string? GrnNumber { get; set; }

    [Required]
    [MaxLength(100)]
    public string PalletNo { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Quantity { get; set; }

    [MaxLength(100)]
    public string? StoreLocation { get; set; }

    public DateTime VerifiedAt { get; set; }

    public DateTime CreatedDate { get; set; }
}