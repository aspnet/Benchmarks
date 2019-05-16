// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Benchmarks.Data
{
    internal class BatchUpdateString
    {
        private const int MaxBatch = 500;

        private static string[] _queries = new string[MaxBatch];

        public static string Query(int batchSize)
        {
            if (_queries[batchSize] != null)
            {
                return _queries[batchSize];
            }

            var sb = StringBuilderCache.Acquire();

            sb.Append("UPDATE world SET randomNumber = temp.randomNumber FROM (VALUES ");

            for (var i = 0; i < batchSize; i++)
            {
                sb.Append($"(@Id_{i}, @Random_{i})");

                if (i != batchSize - 1)
                {
                    sb.Append(", ");
                }
            }

            sb.Append(" ORDER BY 1) AS temp(id, randomNumber) WHERE temp.id = world.id");

            return _queries[batchSize] = StringBuilderCache.GetStringAndRelease(sb);
        }
    }
}