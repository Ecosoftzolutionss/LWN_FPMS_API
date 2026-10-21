using System.ComponentModel.DataAnnotations.Schema;

namespace LWN_IPMS_Dashboard.Entities
{
    [Table("FIFO_DashboardPartConfiguration")]
    public class FifoDashboardPartConfiguration
    {
        public int Id { get; set; }

        public int Position { get; set; }

        public int ItemId { get; set; }

        public DateTime ModifiedDate { get; set; } = DateTime.Now;
    }
}