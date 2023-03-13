// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Linq;

namespace PlatformBenchmarks
{
    internal class BatchUpdateString
    {
        private const int MaxBatch = 500;

        public static DatabaseServer DatabaseServer;

        internal static readonly string[] ParamNames = Enumerable.Range(0, MaxBatch * 2).Select(i => $"@p{i}").ToArray();

        private static string[] _queries = new string[MaxBatch + 1];

        public static string Query(int batchSize)
            => _queries[batchSize] is null
                ? CreateBatch(batchSize)
                : _queries[batchSize];

        private static string CreateBatch(int batchSize)
        {
            var sb = StringBuilderCache.Acquire();

            Func<int, string> paramNameGenerator = i => "$" + i;

            sb.AppendLine("UPDATE world SET randomNumber = CASE id");
            for (var i = 0; i < batchSize * 2;)
            {
                sb.AppendLine($"when {paramNameGenerator(++i)} then {paramNameGenerator(++i)}");
            }
            sb.AppendLine("else randomnumber");
            sb.AppendLine("end");
            sb.Append("where id in (");
            for (var i = 1; i < batchSize * 2; i += 2)
            {
                sb.Append(paramNameGenerator(i));
                if (i < batchSize * 2 - 1)
                {
                    sb.AppendLine(", ");
                }
            }
            sb.Append(")");

            return _queries[batchSize] = StringBuilderCache.GetStringAndRelease(sb);
        }
    }
}
