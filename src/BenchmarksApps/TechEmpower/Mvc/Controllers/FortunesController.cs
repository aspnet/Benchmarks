using Microsoft.AspNetCore.Mvc;
using Mvc.Database;

namespace Mvc.Controllers;

public class FortunesController : Controller
{
    [Route("fortunes")]
    public async Task<IActionResult> Index([FromServices] Db db)
    {
        var fortunes = await db.LoadFortunesRows();

        return View(fortunes);
    }
}
