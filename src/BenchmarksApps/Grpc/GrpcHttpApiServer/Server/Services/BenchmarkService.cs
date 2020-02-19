// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;
using System.Linq;
using System.Threading.Tasks;
using Benchmark;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Server
{
    public class BenchmarkService : Benchmark.BenchmarkService.BenchmarkServiceBase
    {
        private static readonly DateTimeOffset BaseDateTime = new DateTimeOffset(new DateTime(2019, 04, 23));

        private static readonly EntriesCollection _entries;

        static BenchmarkService()
        {
            var entries = Enumerable.Range(1, 8).Select(i => new Entry
            {
                Attributes = new Attributes
                {
                    Created = BaseDateTime.AddDays(i).ToTimestamp(),
                    Enabled = true,
                    Expires = BaseDateTime.AddDays(i).AddYears(1).ToTimestamp(),
                    NotBefore = BaseDateTime.ToTimestamp(),
                    RecoveryLevel = "Purgeable",
                    Updated = BaseDateTime.AddSeconds(i).ToTimestamp(),
                },
                ContentType = "application/xml",
                Id = "https://benchmarktest.id/item/value" + i,
                Tags = { "test", "perf", "json" },
            }).ToList();

            _entries = new EntriesCollection();
            _entries.Entries.AddRange(entries);
        }

        public override Task<HelloReply> Json(Empty request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = "Hello, World!" });
        }

        public override Task<EntriesCollection> Json2k(Empty request, ServerCallContext context)
        {
            return Task.FromResult(_entries);
        }

        public override Task<Empty> JsonInput(EntriesCollection request, ServerCallContext context)
        {
            return Task.FromResult(new Empty());
        }
    }
}
