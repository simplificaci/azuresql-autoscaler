
namespace AzuresqlAutoscaler
{


    public enum SearchDirection
    {
        Next,
        Previous
    }

    public class AutoScalerConfiguration
    {

        public int vCoreMin = int.Parse(Environment.GetEnvironmentVariable("_vCoreMin"));
        public int vCoreMax = int.Parse(Environment.GetEnvironmentVariable("_vCoreMax"));
        public decimal LowCpuPercent = decimal.Parse(Environment.GetEnvironmentVariable("_LowCpuPercent"));
        public decimal HighCpuPercent = decimal.Parse(Environment.GetEnvironmentVariable("_HighCpuPercent"));
        public decimal LowWorkersPercent = decimal.Parse(Environment.GetEnvironmentVariable("_LowWorkersPercent"));
        public decimal HighWorkersPercent = decimal.Parse(Environment.GetEnvironmentVariable("_HighWorkersPercent"));
        public int RequiredDataPoints = 0;

        public AutoScalerConfiguration(string scale)
        {
            RequiredDataPoints = int.Parse(Environment.GetEnvironmentVariable($"_RequiredDataPointsScale{scale}"));

        }

    }

}
