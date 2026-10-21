using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace LWN_IPMS_Dashboard.Entities
{
    [Table("FIFO_DashboardSettings")]
    public class FifoDashboardSetting
    {
        public int Id { get; set; }

        public int DisplaySeconds { get; set; } = 5;

        public int DisplayRows { get; set; } = 8;

        public DateTime ModifiedDate { get; set; } = DateTime.Now;
        [MaxLength(500)]
        public string InputPath { get; set; } = string.Empty;
    }
}