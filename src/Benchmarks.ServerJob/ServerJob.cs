// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Repository;

namespace Benchmarks.ServerJob
{
    public class ServerJob : IIdentifiable
    {
        public int Id { get; set; }

        public string ConnectionFilter { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Framework Framework { get; set; }

        public int? KestrelThreadCount { get; set; }

        public bool? KestrelThreadPoolDispatching { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Scenario Scenario { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public Scheme Scheme { get; set; }

        private IEnumerable<Source> _sources;
        public IEnumerable<Source> Sources
        {
            get
            {
                return _sources ?? Enumerable.Empty<Source>();
            }
            set
            {
                _sources = value;
            }
        }

        [JsonConverter(typeof(StringEnumConverter))]
        public ServerState State { get; set; }

        public string Url { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public WebHost WebHost { get; set; }
    }
}
