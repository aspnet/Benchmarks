// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using Dapper;

namespace Benchmarks.Data
{
    [Table("world")]
    public class World
    {
        [UseColumnAttribute] // Fixes DAP043 warning
        [Column("id")]
        public int Id { get; set; }

        [IgnoreDataMember]
        [NotMapped, DbValue(Ignore = true)]
        public int _Id { get; set; }

        [UseColumnAttribute] // Fixes DAP043 warning
        [Column("randomnumber")]
        public int RandomNumber { get; set; }
    }
}