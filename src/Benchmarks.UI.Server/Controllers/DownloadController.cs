using System.IO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Benchmarks.UI.Server.Controllers
{
    [Route("api/download")]
    public class DownloadController : ControllerBase
    {
        private string _driverPath;

        public DownloadController(IConfiguration configuration)
        {
            _driverPath = configuration["driverPath"];
        }
        
        [HttpGet]
        [Route("{filename}")]
        [RequestSizeLimit(100_000_000)]
        public IActionResult Download(string filename)
        {
            return File(System.IO.File.OpenRead(Path.Combine(_driverPath, filename)), "application/object");
        }
    }
}
