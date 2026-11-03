using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;

namespace Rewrite.Data
{
    [Table("RewriteRule")]
    public class RewriteRule
    {
        [Column("id")]
        public int Id { get; set; }


        [Column("regex")]
        [MaxLength(2048)]
        public string? Regex { get; set; }

        [Column("replacement")]
        [MaxLength(2048)]
        public  string? Replacement { get; set; }
    }
}
