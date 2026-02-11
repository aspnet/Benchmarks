using System.Web.Mvc;
using Newtonsoft.Json;

namespace MvcFull.Controllers
{
    public class HomeController : Controller
    {
        public ActionResult Index()
        {
            return View();
        }

        [HttpGet]
        [Route("plaintext")]
        public ContentResult Plaintext()
        {
            return Content("Hello, World!", "text/plain");
        }

        [HttpGet]
        [Route("json")]
        public ContentResult Json()
        {
            var data = new { message = "Hello, World!" };
            var json = JsonConvert.SerializeObject(data);
            return Content(json, "application/json");
        }
    }
}
