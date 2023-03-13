// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;

namespace PlatformBenchmarks
{
    public readonly struct Fortune : IComparable<Fortune>, IComparable
    {
        public Fortune(int id, byte[] message)
        {
            Id = id;
            MessageUtf8 = message;
        }

        public int Id { get; }

        public byte[] MessageUtf8 { get; }

        public int CompareTo(object obj) => throw new InvalidOperationException("The non-generic CompareTo should not be used");

        // Performance critical, using culture insensitive comparison
        public int CompareTo(Fortune other) => MessageUtf8.AsSpan().SequenceCompareTo(other.MessageUtf8.AsSpan());
    }
}
