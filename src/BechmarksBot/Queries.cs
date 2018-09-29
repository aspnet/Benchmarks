namespace BechmarksBot
{
    public class Queries
    {
        public const string Regressions = @"
            DECLARE @startDate DateTime = DATEADD(month, -1, GETDATE())
            DECLARE @oneMonthAgo DateTime = DATEADD(month, -1, GETDATE())
            DECLARE @oneWeekAgo DateTime = DATEADD(day, -7, GETDATE())
            DECLARE @threeMonthsAgo DateTime = DATEADD(month, -3, GETDATE())
            DECLARE @now DateTime = GETDATE()
            DECLARE @oneDayAgo DateTime = DATEADD(day, -1, GETDATE())
            DECLARE @threeDaysAgo DateTime = DATEADD(day, -3, GETDATE())
            DECLARE @tenDaysAgo DateTime = DATEADD(day, -10, GETDATE())

            -- Only measurements on Physical hardware is taken into account.
            -- The STDEV is measured for each scenario from all the values in the previous week.
            -- We measure the deviation between two measurements in percentage of the STDEV. For instance -100 meansthe RPS went down by the value of the STDEV.
            -- We flag a regression if:
            --  - The three measures in a row have a deviation below -200 compared to the same measurement
            --  - The baseline measurement was not a spike, i.e. its deviation is below +100.

            SELECT *
            FROM
            (
                SELECT [Current].*, BaseLines.STDEV, 
                    (RequestsPerSecond - PreviousRPS3) * 100 / STDEV as [PDev1], 
                    (PreviousRPS1 - PreviousRPS3) * 100 / STDEV as [PDev2], 
                    (PreviousRPS2 - PreviousRPS3) * 100 / STDEV as [PDev3], 
                    (PreviousRPS3 - PreviousRPS4) * 100 / STDEV as [PDev4] 
                FROM
                (
                    SELECT Scenario, Hardware, OperatingSystem, Scheme, WebHost, [DateTime], 
                        LAG(RequestsPerSecond, 1, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS PreviousRPS1,
                        LAG(RequestsPerSecond, 2, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS PreviousRPS2,
                        LAG(RequestsPerSecond, 3, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS PreviousRPS3,
                        LAG(RequestsPerSecond, 4, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS PreviousRPS4,
                        [RequestsPerSecond]
                    FROM [dbo].[AspNetBenchmarksTrends]
                    WHERE [DateTime] > @tenDaysAgo
                    AND Hardware = 'Physical'
                ) AS [Current]
                INNER JOIN 
                (
                    -- Standard deviations on trends
                    SELECT DISTINCT Scenario, Hardware, OperatingSystem, Scheme, WebHost, STDEV([RequestsPerSecond]) OVER (PARTITION BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY Scenario) As STDEV
                    FROM [dbo].[AspNetBenchmarksTrends]
                    WHERE [DateTime] > @oneWeekAgo
                    AND Hardware = 'Physical'
                ) AS Baselines
                ON [Current].Scenario = Baselines.Scenario
                -- AND [Current].Hardware = Baselines.Hardware
                AND [Current].OperatingSystem = Baselines.OperatingSystem
                AND [Current].Scheme = Baselines.Scheme
                AND [Current].WebHost = Baselines.WebHost

                -- Ignore rows without previous values
                WHERE PreviousRPS1 != 0 AND PreviousRPS2 !=0
   
            ) AS Results
            WHERE PDev1 < -200 AND PDev2 < -200 AND PDev3 < -200 AND PDev4 <= 100
            ORDER BY [DateTime] DESC, [PDev1] + [PDev2] + [PDev3], Scenario, Hardware, OperatingSystem, Scheme, WebHost
        ";
    }
}
