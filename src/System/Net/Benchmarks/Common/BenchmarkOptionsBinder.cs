// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.CommandLine;
using System.CommandLine.Binding;
using System.CommandLine.Parsing;

namespace System.Net.Benchmarks;

public abstract class BenchmarkOptionsBinder<TOptions> : BinderBase<TOptions>
    where TOptions : new()
{
    public abstract void AddCommandLineArguments(RootCommand command);

    protected abstract void BindOptions(TOptions options, ParseResult parsed);

    protected override TOptions GetBoundValue(BindingContext bindingContext)
    {
        var options = new TOptions();
        BindOptions(options, bindingContext.ParseResult);
        return options;
    }
}
