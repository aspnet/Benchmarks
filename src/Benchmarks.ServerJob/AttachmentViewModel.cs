using Microsoft.AspNetCore.Http;

namespace Benchmarks.ServerJob
{
    public class AttachmentViewModel
    {
        public int Id { get; set; }
        public string DestinationFilename { get; set; }
        public AttachmentLocation Location { get; set; }
        public IFormFile Content { get; set; }
    }
}
