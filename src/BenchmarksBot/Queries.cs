namespace BenchmarksBot
{
    public class Queries
    {
        public const string Regressions = @"
            DECLARE @startDate DateTime = DATEADD(day, -7, GETDATE())           -- when we start detected issues
            DECLARE @stdevStartDate DateTime = DATEADD(day, -14, @startDate)    -- how long before @startDate we use to measure Std. Dev
            DECLARE @deviationTolerance int = -200;                             -- percentage of Std. Dev is tolerated to flag a measure as abnormal

            -- Only measurements on Physical hardware is taken into account.
            -- The STDEV is measured for each scenario from all the values in the previous week of the measurement.
            -- We measure the deviation between two measurements in percentage of the STDEV. For instance -100 meansthe RPS went down by the value of the STDEV.
            -- We flag a regression if:
            --  - The three measures in a row have a deviation below -@deviationTolerance compared to the same measurement
            --  - The baseline measurement was not a spike, i.e. its deviation is below +100 for two measures.

            SELECT *
            FROM
            (
                SELECT [Current].*, 
                    BaseLines.STDEV,                                                -- standard deviation
                    (RequestsPerSecond - PreviousRPS3) * 100 / STDEV as [PDev1],    -- 3 measures after inflection point
                    (PreviousRPS1 - PreviousRPS3) * 100 / STDEV as [PDev2],         -- 2 measures after inflection point
                    (PreviousRPS2 - PreviousRPS3) * 100 / STDEV as [PDev3],         -- 1 measures after inflection point
                    (PreviousRPS3 - PreviousRPS4) * 100 / STDEV as [PDev4],         -- 1 measures before inflection point
                    (PreviousRPS3 - PreviousRPS5) * 100 / STDEV as [PDev5]          -- 2 measures before inflection point
                FROM
                (
                    SELECT Scenario, Hardware, OperatingSystem, Scheme, WebHost, [DateTime], [Session],
                        LAG(RequestsPerSecond, 1, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS PreviousRPS1,
                        LAG(RequestsPerSecond, 2, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS PreviousRPS2,
                        LAG(RequestsPerSecond, 3, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS PreviousRPS3, -- The inflection measure
                        LAG(RequestsPerSecond, 4, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS PreviousRPS4,
                        LAG(RequestsPerSecond, 5, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS PreviousRPS5,
                        [RequestsPerSecond],
                        LAG([AspNetCoreVersion], 3, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS [PreviousAspNetCoreVersion],
                        LAG([AspNetCoreVersion], 2, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS [CurrentAspNetCoreVersion],
                        LAG([RuntimeVersion], 3, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS [PreviousRuntimeVersion],
                        LAG([RuntimeVersion], 2, 0) OVER (Partition BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY [DateTime]) AS [CurrentRuntimeVersion]
                    FROM [dbo].[@table]
                    WHERE [DateTime] > @startDate
                    AND Hardware = 'Physical'
                ) AS [Current]
                INNER JOIN 
                (
                    -- Standard deviations on trends
                    SELECT DISTINCT Scenario, Hardware, OperatingSystem, Scheme, WebHost, STDEV([RequestsPerSecond]) OVER (PARTITION BY Scenario, Hardware, OperatingSystem, Scheme, WebHost ORDER BY Scenario) As STDEV
                    FROM [dbo].[@table]
                    WHERE [DateTime] > @stdevStartDate and [DateTime] <= @startDate
                    AND Hardware = 'Physical'
                ) AS Baselines
                ON [Current].Scenario = Baselines.Scenario
                AND [Current].Hardware = Baselines.Hardware
                AND [Current].OperatingSystem = Baselines.OperatingSystem
                AND [Current].Scheme = Baselines.Scheme
                AND [Current].WebHost = Baselines.WebHost

                -- Ignore rows without previous values
                WHERE PreviousRPS5 != 0   
            ) AS Results
            WHERE 
                -- thre abnormal measurements in a row
                PDev1 < @deviationTolerance AND PDev2 < @deviationTolerance AND PDev3 < @deviationTolerance
                -- two precending measurements inside Std. Dev
                AND PDev4 <= 100 AND PDev5 <= 100
                -- there is actually a change in the framework                                                  
                AND ([PreviousAspNetCoreVersion] != [CurrentAspNetCoreVersion] OR [PreviousRuntimeVersion] != [CurrentRuntimeVersion])
            ORDER BY [DateTime] DESC
        ";

        public const string Error = @"
            DECLARE @startDate DateTime = DATEADD(day, -7, GETDATE())           -- to find any scenario that has worked in the last checked period

            SELECT Scenario, Hardware, OperatingSystem, Scheme, WebHost, [LastDateTime], [Errors]
            FROM (
                SELECT Scenario, Hardware, OperatingSystem, Scheme, WebHost, Max([DateTime]) [LastDateTime], Max([SocketErrors] + [BadResponses]) as [Errors]
                FROM [dbo].[@table]
                WHERE [DateTime] >= @startDate
                AND [SocketErrors] + [BadResponses] > [RequestsPerSecond] * 1 / 100
                GROUP BY Scenario, Hardware, OperatingSystem, Scheme, WebHost
            ) DATA
            ORDER BY Scenario
        ";

        public const string NotRunning = @"
            DECLARE @startDate DateTime = DATEADD(day, -7, GETDATE())           -- to find any scenario that has worked in the last checked period
            DECLARE @lastDate DateTime = DATEADD(hour, -36, GETDATE())          -- if any of these scenarios hasn't worked in the last 36 hours

            SELECT Scenario, Hardware, OperatingSystem, Scheme, WebHost, [LastDateTime], [Errors]
            FROM (
                SELECT Scenario, Hardware, OperatingSystem, Scheme, WebHost, Max([DateTime]) [LastDateTime], Max([SocketErrors] + [BadResponses]) as [Errors]
                FROM [dbo].[@table]
                WHERE [DateTime] >= @startDate
                GROUP BY Scenario, Hardware, OperatingSystem, Scheme, WebHost
            ) DATA
            WHERE [LastDateTime] <= @lastDate
            ORDER BY Scenario
        ";
    }
}
