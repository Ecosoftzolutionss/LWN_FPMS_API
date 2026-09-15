using System;

namespace DFN_BMS.Models
{
    public class PalletIssueLock
    {
        public int Id { get; set; }

        public int GrnPalletId { get; set; }

        public string UserName { get; set; } = "";

        public string DeviceId { get; set; } = "";

        public decimal Quantity { get; set; }

        public DateTime LockedAt { get; set; }

        public bool IsActive { get; set; }
    }
}