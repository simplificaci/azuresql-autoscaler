
namespace Azure.SQL.DB.Hyperscale.Tools.Classes
{
    public class UsageInfo
    {
        public DateTime TimeStamp = DateTime.Now;
        public string ServiceObjective = string.Empty;
        public decimal AvgCpuPercent = 0;
        public decimal MovingAvgCpuPercent = 0;
        public decimal WorkersPercent = 0;
        public decimal MovingAvgWorkersPercent = 0;
        public int DataPoints = 0;
    }

}
