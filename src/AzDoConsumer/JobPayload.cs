// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Text;
using System.Text.Json.Serialization;

namespace AzDoConsumer
{

    public class JobPayload
    {
        private static TimeSpan DefaultJobTimeout = TimeSpan.FromMinutes(10);

        [JsonConverter(typeof(TimeSpanConverter))]
        public TimeSpan Timeout { get; set; } = DefaultJobTimeout;

        public string Name { get; set; }
        public string[] Args { get; set; }

        public string RawPayload { get; set; }

        public static JobPayload Deserialize(byte[] data)
        {
            try
            {
                var str = Encoding.UTF8.GetString(data);

                // Azure Devops adds a DataContractSerializer preamble to the message, and also
                // an invalid JSON char at the end of the message
                str = str.Substring(str.IndexOf("{"));
                str = str.Substring(0, str.LastIndexOf("}") + 1);
                var result = System.Text.Json.JsonSerializer.Deserialize<JobPayload>(str, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                result.RawPayload = str;

                return result;
            }
            catch (Exception e)
            {
                throw new Exception("Error while parsing message body: " + Encoding.UTF8.GetString(data), e);
            }
        }
    }
}
