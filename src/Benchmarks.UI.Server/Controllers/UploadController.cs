using System.IO;
using System.Threading.Tasks;
using Benchmarks.UI.Server.Models;
using Microsoft.AspNetCore.Mvc;

namespace Benchmarks.UI.Server.Controllers
{
    [Route("api/upload")]
    public class UploadController : ControllerBase
    {
        [HttpPost]
        [Route("file")]
        [RequestSizeLimit(100_000_000)]
        public async Task<IActionResult> UploadFile(NugetPackageAttachment attachment)
        {
            var tempFileName = Path.GetTempFileName();

            using (var writer = System.IO.File.OpenWrite(tempFileName))
            {
                await attachment.Content.CopyToAsync(writer);
            }

            return Ok(tempFileName);
        }
    }
}
