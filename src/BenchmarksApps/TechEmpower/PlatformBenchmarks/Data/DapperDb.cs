using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Dapper;
using Npgsql;

namespace PlatformBenchmarks
{
    public sealed class DapperDb
    {
        private readonly string _connectionString;

        public DapperDb(AppSettings appSettings)
            => _connectionString = appSettings.ConnectionString;

        public async Task<List<FortuneDapper>> LoadFortunesRows()
        {
            List<FortuneDapper> result;

            using (var db = new NpgsqlConnection(_connectionString))
            {
                // Note: don't need to open connection if only doing one thing; let dapper do it
                result = (await db.QueryAsync<FortuneDapper>("SELECT id, message FROM fortune")).AsList();
            }

            result.Add(new FortuneDapper(id: 0, message: "Additional fortune added at request time." ));
            result.Sort();

            return result;
        }
    }
}
