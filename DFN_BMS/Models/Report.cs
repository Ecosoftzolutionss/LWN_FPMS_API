using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DFN_BMS.Models
{
    [Table("Reports")]
    public class Report
    {
        [Key]
        public int Report_ID { get; set; }
        public string? Despatch_ID { get; set; }
        public string? Invoice_Number { get; set; }
        public string? Part_Number { get; set; }
        public int Invoice_Qty { get; set; }
        public int Box_Qty { get; set; }
        public int Part_Qty { get; set; }
        public string? Status { get; set; }
        public string? User_Name { get; set; }
        public int Gate { get; set; }
        public string? Gate_Out_Status { get; set; }
        public string? Gate_Out_No { get; set; }
        public DateTime Created_Date { get; set; }
    }
}