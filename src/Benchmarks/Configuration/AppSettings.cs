// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace Benchmarks.Configuration
{
    public class AppSettings
    {
        public string AiInstrumentationKey { get; set; }

        public bool EnableAi { get; set; }

        public string ConnectionString { get; set; }

        public DatabaseServer Database { get; set; } = DatabaseServer.None;
    }
}
