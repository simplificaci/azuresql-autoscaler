using System;
using System.Collections.Generic;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Dapper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace Azure.SQL.DB.Hyperscale.Tools.Classes
{
    public static class AutoScaler
    {
        public static readonly List<string> GEN4 = new List<string>() { "hs_gen4_1", "hs_gen4_2", "hs_gen4_3", "hs_gen4_4", "hs_gen4_5", "hs_gen4_6", "hs_gen4_7", "hs_gen4_8", "hs_gen4_9", "hs_gen4_10", "hs_gen4_16", "hs_gen4_24" };

        public static readonly List<string> GEN5 = new List<string>() { "hs_gen5_2", "hs_gen5_4", "hs_gen5_6", "hs_gen5_8", "hs_gen5_10", "hs_gen5_12", "hs_gen5_14", "hs_gen5_16", "hs_gen5_18", "hs_gen5_20", "hs_gen5_24", "hs_gen5_32", "hs_gen5_40", "hs_gen5_80" };


        // 
        //https://learn.microsoft.com/en-us/azure/azure-sql/database/resource-limits-vcore-single-databases?view=azuresql#gen5-hardware-part-1-2

        public static Dictionary<int, List<string>> HyperscaleSLOs = new Dictionary<int, List<string>>();

        enum Scaler
        {
            Up,
            Down,
        }

        static AutoScaler()
        {
            HyperscaleSLOs.Add(4, GEN4);
            HyperscaleSLOs.Add(5, GEN5);

        }

        [FunctionName("AutoScaler_Vertical_Up")]
        public static void Vertical_Up([TimerTrigger("*/10 * * * * *", RunOnStartup = true)] TimerInfo timer, ILogger log)
        {
            AutoScalerVerticalRun(Scaler.Up, timer, log);
        }

        [FunctionName("AutoScaler_Vertical_Down")]
        public static void Vertical_Down([TimerTrigger("*/10 * * * * *", RunOnStartup = true)] TimerInfo timer, ILogger log)
        {
            AutoScalerVerticalRun(Scaler.Down, timer, log);
        }

        private static bool ScaleUp(Scaler scaler,
                                       UsageInfo usageInfo,
                                       AutoScalerConfiguration autoscalerConfig,
                                       HyperScaleTier targetSlo,
                                       HyperScaleTier currentSlo,
                                       ILogger log,
                                       SqlConnection conn,
                                       string databaseName)
        {

            if (scaler != Scaler.Up) return false;

            // Scale Up
            //INFO - If the average reaches at least one of the conditions, then the scale up is necessary.
            //INFO - Unlike Scale Down, where all conditions must be met.
            if (usageInfo.MovingAvgCpuPercent > autoscalerConfig.HighCpuPercent ||
                usageInfo.MovingAvgWorkersPercent > autoscalerConfig.HighWorkersPercent)
            {
                targetSlo = GetServiceObjective(currentSlo, SearchDirection.Next);
                if (targetSlo != null && currentSlo.Cores < autoscalerConfig.vCoreMax && currentSlo != targetSlo)
                {
                    if (!Debugger.IsAttached)
                    {
                        log.LogInformation($"HIGH CpuPercent reached: scaling up to {targetSlo}");
                        conn.Execute($"ALTER DATABASE [{databaseName}] MODIFY (SERVICE_OBJECTIVE = '{targetSlo}')");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("HIGH CpuPercent reached! [IGNORED by debugging attached]");
                    }
                }
            }

            return false;
        }


        private static bool ScaleDown(Scaler scaler,
                                         UsageInfo usageInfo,
                                         AutoScalerConfiguration autoscalerConfig,
                                         HyperScaleTier targetSlo,
                                         HyperScaleTier currentSlo,
                                         ILogger log,
                                         SqlConnection conn,
                                         string databaseName)
        {

            if (scaler != Scaler.Down) return false;

            //INFO - Unlike Scale Up, note that here the "AND" condition, this is because it only makes sense to decrease
            //INFO - if all the requirements are lower than expected, while for Scale Up one of them is necessary, so there,
            //INFO - we have an "OR" condition
            // Scale Down
            if (usageInfo.MovingAvgCpuPercent < autoscalerConfig.LowCpuPercent &&
                usageInfo.MovingAvgWorkersPercent < autoscalerConfig.LowWorkersPercent)
            {
                targetSlo = GetServiceObjective(currentSlo, SearchDirection.Previous);
                if (targetSlo != null && currentSlo.Cores > autoscalerConfig.vCoreMin && currentSlo != targetSlo)
                {
                    if (!Debugger.IsAttached)
                    {
                        log.LogInformation($"LOW CpuPercent reached: scaling down to {targetSlo}");
                        conn.Execute($"ALTER DATABASE [{databaseName}] MODIFY (SERVICE_OBJECTIVE = '{targetSlo}')");
                        return true;
                    }
                    else
                    {
                        Console.WriteLine("LOW CpuPercent reached! [IGNORED by debugging attached]");
                    }
                }
            }

            return false;
        }

        private static void AutoScalerVerticalRun(Scaler scaler, TimerInfo timer, ILogger log)
        {

            var autoscalerConfig = new AutoScalerConfiguration(scaler.ToString());

            string connectionString = Environment.GetEnvironmentVariable("_AzureSQLConnection");
            string databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;

            using (var conn = new SqlConnection(connectionString))
            {
                // Get usage data
                var followingRows = autoscalerConfig.RequiredDataPoints - 1;
                var usageInfo = conn.QuerySingleOrDefault<UsageInfo>($@"
                    select top (1)
                        [end_time] as [TimeStamp], 
                        databasepropertyex(db_name(), 'ServiceObjective') as ServiceObjective,
                        [avg_cpu_percent] as AvgCpuPercent, 
                        avg([avg_cpu_percent]) over (order by end_time desc rows between current row and {followingRows} following) as MovingAvgCpuPercent,
                        [max_worker_percent] as WorkersPercent, 
                        avg([max_worker_percent]) over (order by end_time desc rows between current row and {followingRows} following) as MovingAvgWorkersPercent,
                        count(*) over (order by end_time desc rows between current row and {followingRows} following) as DataPoints
                    from 
                        sys.dm_db_resource_stats
                    order by 
                        end_time desc 
                ");

                // If SLO is happening result could be null
                if (usageInfo == null)
                {
                    log.LogInformation("No information received from server.");
                    return;
                }

                if (Debugger.IsAttached)
                {
                    Console.WriteLine($"MovingAvgCpuPercent: {usageInfo.MovingAvgCpuPercent}");
                    Console.WriteLine($"MovingAvgWorkersPercent: {usageInfo.MovingAvgWorkersPercent}");
                }

                // Decode current SLO
                var currentSlo = HyperScaleTier.Parse(usageInfo.ServiceObjective);
                var targetSlo = currentSlo;

                // At least one minute of historical data is needed
                if (usageInfo.DataPoints < autoscalerConfig.RequiredDataPoints)
                {
                    EnoughData(log, usageInfo, currentSlo, targetSlo, conn);
                    return;
                }

                ScaleDown(scaler, usageInfo, autoscalerConfig, targetSlo, currentSlo, log, conn, databaseName);
                ScaleUp(scaler, usageInfo, autoscalerConfig, targetSlo, currentSlo, log, conn, databaseName);

                WriteMetrics(log, usageInfo, currentSlo, targetSlo);

            }

        }

        private static void EnoughData(ILogger log, UsageInfo usageInfo, HyperScaleTier currentSlo, HyperScaleTier targetSlo, SqlConnection conn)
        {
            log.LogInformation("Not enough data points.");
            WriteMetrics(log, usageInfo, currentSlo, targetSlo);
            Console.WriteLine("Not enough data points.");
        }

        private static void WriteMetrics(ILogger log, UsageInfo usageInfo, HyperScaleTier currentSlo, HyperScaleTier targetSlo)
        {
            log.LogMetric("DataPoints", usageInfo.DataPoints);
            log.LogMetric("AvgCpuPercent", Convert.ToDouble(usageInfo.AvgCpuPercent));
            log.LogMetric("MovingAvgCpuPercent", Convert.ToDouble(usageInfo.MovingAvgCpuPercent));
            log.LogMetric("WorkersPercent", Convert.ToDouble(usageInfo.WorkersPercent));
            log.LogMetric("MovingAvgWorkersPercent", Convert.ToDouble(usageInfo.MovingAvgWorkersPercent));
            log.LogMetric("CurrentCores", Convert.ToDouble(currentSlo.Cores));
            log.LogMetric("TargetCores", Convert.ToDouble(targetSlo.Cores));

            Console.WriteLine(
                "\nDataPoints:" + usageInfo.DataPoints.ToString() +
                "\nAvgCpuPercent:" + Convert.ToDouble(usageInfo.AvgCpuPercent) +
                "\nMovingAvgCpuPercent:" + Convert.ToDouble(usageInfo.MovingAvgCpuPercent) +
                "\nCurrentCores:" + Convert.ToDouble(currentSlo.Cores) +
                "\nTargetCores:" + Convert.ToDouble(targetSlo.Cores));

        }

        public static HyperScaleTier GetServiceObjective(HyperScaleTier currentSLO, SearchDirection direction)
        {
            var targetSLO = currentSLO;
            var availableSlos = HyperscaleSLOs[currentSLO.Generation];
            var index = availableSlos.IndexOf(currentSLO.ToString().ToLower());

            if (direction == SearchDirection.Next && index < availableSlos.Count)
                targetSLO = HyperScaleTier.Parse(availableSlos[index + 1]);

            if (direction == SearchDirection.Previous && index > 0)
                targetSLO = HyperScaleTier.Parse(availableSlos[index - 1]);

            return targetSLO;
        }
    }
}
