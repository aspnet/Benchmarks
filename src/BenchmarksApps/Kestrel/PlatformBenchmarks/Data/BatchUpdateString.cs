// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Linq;

namespace PlatformBenchmarks
{
    internal class BatchUpdateString
    {
        private const int MaxBatch = 500;

        public static DatabaseServer DatabaseServer;

        internal static readonly string[] Ids = Enumerable.Range(0, MaxBatch).Select(i => $"@Id_{i}").ToArray();
        internal static readonly string[] Randoms = Enumerable.Range(0, MaxBatch).Select(i => $"@Random_{i}").ToArray();

        private static readonly string[] _queries = new string[MaxBatch];

        public static string Query(int batchSize)
        {
            if (_queries[batchSize] != null)
            {
                return _queries[batchSize];
            }

            var lastParam = batchSize * 2;

            var sb = StringBuilderCache.Acquire();

            if (DatabaseServer == DatabaseServer.PostgreSql)
            {
                sb.Append("UPDATE world SET randomNumber = CASE id ");
                Enumerable.Range(1, batchSize * 2).Where(x => x % 2 == 1).ToList().ForEach(i => sb.Append($"when ${i.ToString()} then ${(i + 1).ToString()} "));
                sb.AppendLine("else randomnumber end");
                sb.Append("where id in (");
                Enumerable.Range(1, batchSize * 2).Where(x => x % 2 == 1).ToList().ForEach(i => sb.Append($"${i.ToString()}{(lastParam == i + 1 ? "" : ", ")}"));
                sb.Append(")");
            }
            else
            {
                Enumerable.Range(0, batchSize).ToList().ForEach(i => sb.Append($"UPDATE world SET randomnumber = @Random_{i} WHERE id = @Id_{i};"));
            }

            return _queries[batchSize] = StringBuilderCache.GetStringAndRelease(sb);
        }
    }
}