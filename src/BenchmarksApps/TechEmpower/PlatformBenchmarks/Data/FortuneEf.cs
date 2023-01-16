using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;

namespace PlatformBenchmarks
{
    [Table("fortune")]
    public class FortuneEf : IComparable<FortuneEf>, IComparable
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("message")]
        [StringLength(2048)]
        [IgnoreDataMember]
        [Required]
        public string Message { get; set; }

        public int CompareTo(object obj)
        {
            return CompareTo((FortuneEf)obj);
        }

        public int CompareTo(FortuneEf other)
        {
            return String.CompareOrdinal(Message, other.Message);
        }
    }
}