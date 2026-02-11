using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;

namespace MvcFull.Models
{
    [Table("fortune")]
    public class Fortune : System.IComparable<Fortune>, System.IComparable
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
            return CompareTo((Fortune)obj);
        }

        public int CompareTo(Fortune other)
        {
            return string.CompareOrdinal(Message, other?.Message);
        }
    }
}
