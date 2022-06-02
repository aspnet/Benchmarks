using System;
using System.CommandLine;
using System.CommandLine.Binding;

namespace HttpClientBenchmarks
{
    public class ClientOptionsBinder : BinderBase<ClientOptions>
    {
        public static Option<string> AddressOption { get; } = new Option<string>("--address", "The server address to request") { IsRequired = true };
        public static Option<string> PortOption { get; } = new Option<string>("--port", "The server port to request") { IsRequired = true };
        public static Option<bool> UseHttpsOption { get; } = new Option<bool>("--useHttps", () => false, "Whether to use HTTPS");
        public static Option<string> PathOption { get; } = new Option<string>("--path", () => "/", "The server path to request");
        public static Option<Version> HttpVersionOption { get; } = new Option<Version>("--httpVersion", "HTTP Version (1.1 or 2.0 or 3.0)") { IsRequired = true };
        public static Option<int> NumberOfHttpClientsOption { get; } = new Option<int>("--numberOfHttpClients", () => 1, "Number of HttpClients");
        public static Option<int> ConcurrencyPerHttpClientOption { get; } = new Option<int>("--concurrencyPerHttpClient", () => 1, "Number of concurrect requests per one HttpClient");
        public static Option<int> Http11MaxConnectionsPerServerOption { get; } = new Option<int>("--http11MaxConnectionsPerServer", () => 0, "Max number of HTTP/1.1 connections per server, 0 for unlimited");
        public static Option<bool> Http20EnableMultipleConnectionsOption { get; } = new Option<bool>("--http20EnableMultipleConnections", () => true, "Enable multiple HTTP/2.0 connections");
        public static Option<bool> UseWinHttpHandlerOption { get; } = new Option<bool>("--useWinHttpHandler", () => false, "Use WinHttpHandler instead of SocketsHttpHandler");
        public static Option<bool> UseHttpMessageInvokerOption { get; } = new Option<bool>("--useHttpMessageInvoker", () => false, "Use HttpMessageInvoker instead of HttpClient");
        public static Option<bool> CollectRequestTimingsOption { get; } = new Option<bool>("--collectRequestTimings", () => false, "Collect percentiled metrics of request timings");
        public static Option<string> ScenarioOption { get; } = new Option<string>("--scenario", "Scenario to run ('get', 'post')") { IsRequired = true };
        public static Option<int> ContentSizeOption { get; } = new Option<int>("--contentSize", () => 0, "Request Content size, 0 for no Content");
        public static Option<int> ContentWriteSizeOption { get; } = new Option<int>("--contentWriteSize", () => ClientOptions.DefaultBufferSize, "Request Content single write size, also chunk size if chunked encoding is used");
        public static Option<bool> ContentFlushAfterWriteOption { get; } = new Option<bool>("--contentFlushAfterWrite", () => false, "Flush request content stream after each write");
        public static Option<bool> ContentUnknownLengthOption { get; } = new Option<bool>("--contentUnknownLength", () => false, "False to send Content-Length header, true to use chunked encoding for HTTP/1.1 or unknown content length for HTTP/2.0 and 3.0");
        public static Option<string[]> HeaderOption { get; } = new Option<string[]>(new string[] { "-H", "--header" }, "Custom headers, multiple values allowed");
        public static Option<int> GeneratedStaticHeadersCountOption { get; } = new Option<int>("--generatedStaticHeadersCount", () => 0, "Number of generated request headers with static values");
        public static Option<int> GeneratedDynamicHeadersCountOption { get; } = new Option<int>("--generatedDynamicHeadersCount", () => 0, "Number of generated request headers with values changing per each request");
        public static Option<bool> UseDefaultRequestHeadersOption { get; } = new Option<bool>("--useDefaultRequestHeaders", () => false, "Use HttpClient.DefaultRequestHeaders whenever possible");
        public static Option<int> WarmupOption { get; } = new Option<int>("--warmup", () => ClientOptions.DefaultDuration, "Duration of the warmup in seconds");
        public static Option<int> DurationOption { get; } = new Option<int>("--duration", () => ClientOptions.DefaultDuration, "Duration of the test in seconds");

        public static void AddOptionsToCommand(RootCommand command)
        {
            command.AddOption(AddressOption);
            command.AddOption(PortOption);
            command.AddOption(UseHttpsOption);
            command.AddOption(PathOption);
            command.AddOption(HttpVersionOption);
            command.AddOption(NumberOfHttpClientsOption);
            command.AddOption(ConcurrencyPerHttpClientOption);
            command.AddOption(Http11MaxConnectionsPerServerOption);
            command.AddOption(Http20EnableMultipleConnectionsOption);
            command.AddOption(UseWinHttpHandlerOption);
            command.AddOption(UseHttpMessageInvokerOption);
            command.AddOption(CollectRequestTimingsOption);
            command.AddOption(ScenarioOption);
            command.AddOption(ContentSizeOption);
            command.AddOption(ContentWriteSizeOption);
            command.AddOption(ContentFlushAfterWriteOption);
            command.AddOption(ContentUnknownLengthOption);
            command.AddOption(HeaderOption);
            command.AddOption(GeneratedStaticHeadersCountOption);
            command.AddOption(GeneratedDynamicHeadersCountOption);
            command.AddOption(UseDefaultRequestHeadersOption);
            command.AddOption(WarmupOption);
            command.AddOption(DurationOption);
        }

        protected override ClientOptions GetBoundValue(BindingContext bindingContext)
        {
            var parsed = bindingContext.ParseResult;

            var options = new ClientOptions()
            {
                Address = parsed.GetValueForOption(AddressOption),
                Port = parsed.GetValueForOption(PortOption),
                UseHttps = parsed.GetValueForOption(UseHttpsOption),
                Path = parsed.GetValueForOption(PathOption),
                HttpVersion = parsed.GetValueForOption(HttpVersionOption),
                NumberOfHttpClients = parsed.GetValueForOption(NumberOfHttpClientsOption),
                ConcurrencyPerHttpClient = parsed.GetValueForOption(ConcurrencyPerHttpClientOption),
                Http11MaxConnectionsPerServer = parsed.GetValueForOption(Http11MaxConnectionsPerServerOption),
                Http20EnableMultipleConnections = parsed.GetValueForOption(Http20EnableMultipleConnectionsOption),
                UseWinHttpHandler = parsed.GetValueForOption(UseWinHttpHandlerOption),
                UseHttpMessageInvoker = parsed.GetValueForOption(UseHttpMessageInvokerOption),
                CollectRequestTimings = parsed.GetValueForOption(CollectRequestTimingsOption),
                Scenario = parsed.GetValueForOption(ScenarioOption),
                ContentSize = parsed.GetValueForOption(ContentSizeOption),
                ContentWriteSize = parsed.GetValueForOption(ContentWriteSizeOption),
                ContentFlushAfterWrite = parsed.GetValueForOption(ContentFlushAfterWriteOption),
                ContentUnknownLength = parsed.GetValueForOption(ContentUnknownLengthOption),
                GeneratedStaticHeadersCount = parsed.GetValueForOption(GeneratedStaticHeadersCountOption),
                GeneratedDynamicHeadersCount = parsed.GetValueForOption(GeneratedDynamicHeadersCountOption),
                UseDefaultRequestHeaders = parsed.GetValueForOption(UseDefaultRequestHeadersOption),
                Warmup = parsed.GetValueForOption(WarmupOption),
                Duration = parsed.GetValueForOption(DurationOption)
            };

            var headers = parsed.GetValueForOption(HeaderOption) ?? Array.Empty<string>();
            foreach (var header in headers)
            {
                var headerNameValue = header.Split(':', 2, StringSplitOptions.RemoveEmptyEntries);
                string name = headerNameValue[0].Trim();
                string? value = headerNameValue.Length > 1 ? headerNameValue[1].Trim() : null;
                options.Headers.Add((name, value?.Length > 0 ? value : null));
            }

            return options;
        }
    }
}
