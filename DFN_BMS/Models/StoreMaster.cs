using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DFN_BMS.Models
{
    [Table("STORE_MASTER")]
    public class StoreMaster
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(150)]
        public string StoreLocation { get; set; }

        [Required]
        public int PalletTypeId { get; set; }

        [ValidateNever]
        public PalletTypeMaster? PalletType { get; set; }

        [MaxLength(30)]
        [ValidateNever]
        public string? PalletNumber { get; set; }

        /*
         * Colour is automatically assigned from
         * PALLET_TYPE_MASTER.
         *
         * User does NOT enter this value.
         */
        [MaxLength(7)]
        [ValidateNever]
        public string? ColourCode { get; set; }

        public int? PartNumberId { get; set; }

        [ValidateNever]
        public ItemMaster? PartNumber { get; set; }

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? ModifiedDate { get; set; }
    }
}