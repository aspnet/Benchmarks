﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Benchmarks.NetworkStreamBenchmark.Shared;
using System.CommandLine;
using System.CommandLine.Parsing;

namespace System.Net.Benchmarks.NetworkStreamBenchmark.Client;

internal class NetworkStreamClientOptionsBinder : BenchmarkOptionsBinder<NetworkStreamClientOptions>
{
    public static Option<string> AddressOption { get; } = new Option<string>("--address", "The IP address to connect to.") { IsRequired = true };
    public static Option<int> ConnectionsOption { get; } = new Option<int>("--connections", () => 1, "The number of concurrent connections to make.");
    public static Option<double> DurationOption { get; } = new Option<double>("--duration", () => 15, "The duration of the test in seconds.");
    public static Option<double> WarmupOption { get; } = new Option<double>("--warmup", () => 15, "The duration of the warmup in seconds.");

    public override void AddCommandLineArguments(RootCommand command)
    {
        NetworkStreamOptionsHelper.AddOptions(command);
        command.AddOption(AddressOption);
        command.AddOption(ConnectionsOption);
        command.AddOption(DurationOption);
        command.AddOption(WarmupOption);
    }

    protected override void BindOptions(NetworkStreamClientOptions options, ParseResult parsed)
    {
        NetworkStreamOptionsHelper.BindOptions(options, parsed);

        if (!IPAddress.TryParse(parsed.GetValueForOption(AddressOption), out var address))
        {
            throw new Exception("Invalid IP Address.");
        }

        options.Address = address;
        options.Connections = parsed.GetValueForOption(ConnectionsOption);
        options.Duration = TimeSpan.FromSeconds(parsed.GetValueForOption(DurationOption));
        options.Warmup = TimeSpan.FromSeconds(parsed.GetValueForOption(WarmupOption));
    }
}
