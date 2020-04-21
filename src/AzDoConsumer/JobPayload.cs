using System;
using System.Text;

namespace AzDoConsumer
{
    public class JobPayload
    {
        private static TimeSpan DefaultJobTimeout = TimeSpan.FromMinutes(10);

        public TimeSpan Timeout { get; set; } = DefaultJobTimeout;

        public string[] Args { get; set; }

        public string RawPayload { get; set; }

        public static JobPayload Deserialize(byte[] data)
        {
            var str = Encoding.UTF8.GetString(data);
            str = str.Substring(str.IndexOf("{"));
            str = str.Substring(0, str.LastIndexOf("}") + 1);
            var result = System.Text.Json.JsonSerializer.Deserialize<JobPayload>(str, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true } );

            result.RawPayload = str;

            return result;
        }
    }
}
