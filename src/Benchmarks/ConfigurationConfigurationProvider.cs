// Copyright (c) .NET Foundation. All rights reserved. 
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information. 

using System;

namespace Microsoft.Extensions.Configuration
{
    public static class ConfigurationExtensions
    {
        public static IConfigurationBuilder AddConfiguration(this IConfigurationBuilder builder, IConfiguration source)
        {
            return builder.Add(new ConfigurationConfigurationProvider(source));
        }

        public static IConfigurationBuilder AddConfiguration(this IConfigurationBuilder builder, string prefix, IConfiguration source)
        {
            return builder.Add(new ConfigurationConfigurationProvider(prefix, source));
        }
    }

    public class ConfigurationConfigurationProvider : ConfigurationProvider
    {
        public ConfigurationConfigurationProvider(IConfiguration source)
            : this(null, source)
        {

        }

        public ConfigurationConfigurationProvider(string prefix, IConfiguration source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (prefix == null)
            {
                foreach (var pair in source.GetChildren())
                {
                    Data.Add(pair.Key, pair.Value);
                }
            }
            else
            {
                var pairs = source.GetChildren();

                foreach (var pair in pairs)
                {
                    if (pair.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        Data.Add(pair.Key.Substring(prefix.Length), pair.Value);
                    }
                }
            }
        }
    }
}
