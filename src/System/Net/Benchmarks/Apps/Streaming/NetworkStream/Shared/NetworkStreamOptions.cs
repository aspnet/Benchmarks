// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace System.Net.Benchmarks.NetworkStreamBenchmark.Shared;

internal class NetworkStreamOptions
{
    public int ReceiveBufferSize { get; set; }
    public int SendBufferSize { get; set; }
    public int Port { get; set; }
    public Scenario Scenario { get; set; }


    public override string ToString() => $"ReceiveBufferSize: {ReceiveBufferSize}, SendBufferSize: {SendBufferSize}, Port: {Port}, Scenario: {Scenario}";
}
