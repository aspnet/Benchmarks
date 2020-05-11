// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;

namespace Benchmarks.Controllers
{
    [Route("mvc")]
    public class HomeController : Controller
    {
        private static readonly DateTimeOffset BaseDateTime = new DateTimeOffset(new DateTime(2019, 04, 23));

        private static readonly List<Entry> _entries4k = Enumerable.Range(1, 8).Select(i => new Entry
        {
            Attributes = new Attributes
            {
                Created = BaseDateTime.AddDays(i),
                Enabled = true,
                Expires = BaseDateTime.AddDays(i).AddYears(1),
                NotBefore = BaseDateTime,
                RecoveryLevel = "Purgeable",
                Updated = BaseDateTime.AddSeconds(i),
            },
            ContentType = "application/xml",
            Id = "https://benchmarktest.id/item/value" + i,
            Tags = new[] { "test", "perf", "json" },
        }).ToList();

        private static readonly List<Entry> _entries2MB = Enumerable.Range(1, 5250).Select(i => new Entry
        {
            Attributes = new Attributes
            {
                Created = BaseDateTime.AddDays(i),
                Enabled = true,
                Expires = BaseDateTime.AddDays(i).AddYears(1),
                NotBefore = BaseDateTime,
                RecoveryLevel = "Purgeable",
                Updated = BaseDateTime.AddSeconds(i),
            },
            ContentType = "application/xml",
            Id = "https://benchmarktest.id/item/value" + i,
            Tags = new[] { "test", "perf", "json" },
        }).ToList();

        [HttpGet("plaintext")]
        public IActionResult Plaintext()
        {
            return new PlainTextActionResult();
        }

        [HttpGet("json")]
        [Produces("application/json")]
        public object Json()
        {
            return new { message = "Hello, World!" };
        }

        // Note that this produces 4kb data. We're leaving the misnamed scenario as is to avoid loosing historical context
        [HttpGet("json2k")]
        [Produces("application/json")]
        public object Json2k() => _entries4k;

        [HttpGet("json2M")]
        [Produces("application/json")]
        public List<Entry> Json2M() => _entries2MB;

        [HttpPost("jsoninput")]
        [Consumes("application/json")]
        public ActionResult JsonInput([FromBody] List<Entry> entry) => Ok();

        [HttpGet("view")]
        public ViewResult Index()
        {
            return View();
        }

        private class PlainTextActionResult : IActionResult
        {
            private static readonly byte[] _helloWorldPayload = Encoding.UTF8.GetBytes("Hello, World!");

            public Task ExecuteResultAsync(ActionContext context)
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status200OK;
                response.ContentType = "text/plain";
                var payloadLength = _helloWorldPayload.Length;
                response.ContentLength = payloadLength;
                return response.Body.WriteAsync(_helloWorldPayload, 0, payloadLength);
            }
        }
    }

    public partial class Entry
    {
        public Attributes Attributes { get; set; }
        public string ContentType { get; set; }
        public string Id { get; set; }
        public bool Managed { get; set; }
        public string[] Tags { get; set; }
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
