// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;

namespace Benchmarks.Data
{
    [Table("world")]
    public class World
    {
        [Column("id")]
        public int Id { get; set; }

        [IgnoreDataMember]
        [NotMapped]
        public int _Id { get; set; }

        [Column("randomnumber")]
        public int RandomNumber { get; set; }
    }
}