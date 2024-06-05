// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Parsing;


namespace System.Net.Benchmarks.NetworkStreamBenchmark.Shared;

internal static class NetworkStreamOptionsHelper
{
    public static Option<int> ReceiveBufferSizeOption { get; } = new Option<int>("--receive-buffer-size", () => 32 * 1024, "The size of the receive buffer.");
    public static Option<int> SendBufferSizeOption { get; } = new Option<int>("--send-buffer-size", () => 32 * 1024, "The size of the receive buffer, 0 for no writes.");
    public static Option<int> PortOption { get; } = new Option<int>("--port", () => 9998, "The server port to listen on");
    public static Option<Scenario> ScenarioOption { get; } = new Option<Scenario>("--scenario", "The scenario to run") { IsRequired = true };
    public static void AddOptions(RootCommand command)
    {
        command.AddOption(ReceiveBufferSizeOption);
        command.AddOption(SendBufferSizeOption);
        command.AddOption(PortOption);
        command.AddOption(ScenarioOption);
    }

    public static void BindOptions(NetworkStreamOptions options, ParseResult parsed)
    {
        options.ReceiveBufferSize = parsed.GetValueForOption(ReceiveBufferSizeOption);
        options.SendBufferSize = parsed.GetValueForOption(SendBufferSizeOption);
        options.Port = parsed.GetValueForOption(PortOption);
        options.Scenario = parsed.GetValueForOption(ScenarioOption);
    }
}
