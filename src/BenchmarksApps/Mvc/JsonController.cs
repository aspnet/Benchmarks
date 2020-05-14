// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;

namespace Benchmarks.Controllers
{
    [ApiController]
    public class JsonController : Controller
    {
        private static readonly List<Entry> _entries2k = Entry.Create(8).ToList();
        private static List<Entry> _entriesNk;
        private static int previousSizeInBytes;

        [HttpGet("/json-helloworld")]
        [Produces("application/json")]
        public object Json()
        {
            return new { message = "Hello, World!" };
        }

        [HttpGet("/json2k")]
        [Produces("application/json")]
        public List<Entry> Json2k() => _entries2k;

        [HttpGet("/jsonNbytes/{sizeInBytes}")]
        [Produces("application/json")]
        public List<Entry> JsonNk([FromRoute] int sizeInBytes)
        {
            if (_entriesNk is null || sizeInBytes != previousSizeInBytes)
            {
                var numItems = sizeInBytes / 340; // ~ 340 bytes per item
                _entriesNk = Entry.Create(numItems);
                previousSizeInBytes = sizeInBytes;
            }

            return _entriesNk;
        }

        [HttpPost("/jsoninput")]
        [Consumes("application/json")]
        public ActionResult JsonInput([FromBody] List<Entry> entry) => Ok();
    }

    public partial class Entry
    {
        public Attributes Attributes { get; set; }
        public string ContentType { get; set; }
        public string Id { get; set; }
        public bool Managed { get; set; }
        public string[] Tags { get; set; }

        public static List<Entry> Create(int n)
        {
            var baseDateTime = new DateTimeOffset(new DateTime(2019, 04, 23));

            return Enumerable.Range(1, n).Select(i => new Entry
            {
                Attributes = new Attributes
                {
                    Created = baseDateTime.AddDays(i),
                    Enabled = true,
                    Expires = baseDateTime.AddDays(i).AddYears(1),
                    NotBefore = baseDateTime,
                    RecoveryLevel = "Purgeable",
                    Updated = baseDateTime.AddSeconds(i),
                },
                ContentType = "application/xml",
                Id = "https://benchmarktest.id/item/value" + i,
                Tags = new[] { "test", "perf", "json" },
            }).ToList();
        }
    }

    public partial class Attributes
    {
        public DateTimeOffset Created { get; set; }
        public bool Enabled { get; set; }
        public DateTimeOffset Expires { get; set; }
        public DateTimeOffset NotBefore { get; set; }
        public string RecoveryLevel { get; set; }
        public DateTimeOffset Updated { get; set; }
    }
}
