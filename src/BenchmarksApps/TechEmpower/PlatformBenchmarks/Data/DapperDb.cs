using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

[module: DapperAot] // enable AOT Dapper support project-wide
[module: CacheCommand] // reuse DbCommand instances when possible

namespace PlatformBenchmarks
{
    public sealed class DapperDb
    {
        private readonly string _connectionString;

        public DapperDb(AppSettings appSettings)
            => _connectionString = appSettings.ConnectionString;

        public async Task<List<FortuneUtf16>> LoadFortunesRows()
        {
            List<FortuneUtf16> result;

            using (var db = new NpgsqlConnection(_connectionString))
            {
                // Note: don't need to open connection if only doing one thing; let dapper do it
                result = (await db.QueryAsync<FortuneUtf16>("SELECT id, message FROM fortune")).AsList();
            }

            result.Add(new FortuneUtf16(id: 0, message: "Additional fortune added at request time." ));
            result.Sort();

            return result;
        }
    }
}
