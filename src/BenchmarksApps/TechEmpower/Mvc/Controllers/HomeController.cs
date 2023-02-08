using Microsoft.AspNetCore.Mvc;

namespace Mvc.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }

    [HttpGet("plaintext")]
    public string Plaintext()
    {
        return "Hello, World!";
    }

    [HttpGet("json")]
    [Produces("application/json")]
    public object Json()
    {
        return new { message = "Hello, World!" };
    }
}
