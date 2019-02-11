using Microsoft.AspNetCore.Http;

namespace Benchmarks.UI.Server.Models
{
    public class NugetPackageAttachment
    {
        public IFormFile Content { get; set; }
    }
}
