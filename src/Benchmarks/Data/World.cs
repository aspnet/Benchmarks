// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.ComponentModel.DataAnnotations.Schema;
using MongoDB.Bson.Serialization.Attributes;

namespace Benchmarks.Data
{
    [Table("world")]
    public class World
    {
        [Column("id")]
        [BsonElement("id")]
        public int Id { get; set; }

        [BsonId]
        public int _Id { get; set; }

        [Column("randomnumber")]
        [BsonElement("randomNumber")]
        public int RandomNumber { get; set; }
    }
}