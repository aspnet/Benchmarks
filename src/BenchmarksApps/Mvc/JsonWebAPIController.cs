
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace Benchmarks.Controllers
{
    [Route("")]
    public class JsonWebAPIController : ApiController
    {
        private static readonly List<Entry> _entries2k = Entry.Create(8).ToList();
        private static List<Entry> _entriesNk;
        private static int previousSizeInBytes;

        [HttpGet]
        [Route("json-helloworld")]
        public object Json()
        {
            return new { message = "Hello, World!" };
        }

        [HttpGet]
        [Route("json2k")]
        public List<Entry> Json2k() => _entries2k;

        [HttpGet]
        [Route("jsonNbytes/{sizeInBytes}")]
        public List<Entry> JsonNk([FromUri] int sizeInBytes)
        {
            if (_entriesNk is null || sizeInBytes != previousSizeInBytes)
            {
                var numItems = sizeInBytes / 340; // ~ 340 bytes per item
                _entriesNk = Entry.Create(numItems);
                previousSizeInBytes = sizeInBytes;
            }

            return _entriesNk;
        }

        [HttpPost]
        [Route("jsoninput")]
        public IHttpActionResult JsonInput([FromBody] List<Entry> entry) => Ok();
    }
}