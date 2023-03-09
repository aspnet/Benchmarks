// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace PlatformBenchmarks
{
    internal class BatchUpdateString
    {
        private const int MaxBatch = 500;

        private static string[] _queries = new string[MaxBatch + 1];

        public static string Query(int batchSize)
            => _queries[batchSize] is null
                ? CreateBatch(batchSize)
                : _queries[batchSize];

        private static string CreateBatch(int batchSize)
        {
            var sb = StringBuilderCache.Acquire();

#if NET6_0_OR_GREATER
            Func<int, string> paramNameGenerator = i => "$" + i;
#else
            Func<int, string> paramNameGenerator = i => "@p" + i;
#endif

            sb.Append("UPDATE world SET randomNumber = temp.randomNumber FROM (VALUES ");
            var c = 1;
            for (var i = 0; i < batchSize; i++)
            {
                if (i > 0)
                    sb.Append(", ");
                sb.Append($"({paramNameGenerator(c++)}, {paramNameGenerator(c++)})");
            }
            sb.Append(" ORDER BY 1) AS temp(id, randomNumber) WHERE temp.id = world.id");

            return _queries[batchSize] = StringBuilderCache.GetStringAndRelease(sb);
        }
    }
}
