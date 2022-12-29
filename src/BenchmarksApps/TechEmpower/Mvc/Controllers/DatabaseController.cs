using Microsoft.AspNetCore.Mvc;
using Mvc.Database;
using Mvc.Models;

namespace Mvc.Controllers;

public class DatabaseController : Controller
{
    [HttpGet("fortunes")]
    public Task<List<Fortune>> Fortunes([FromServices] Db db)
    {
        return db.LoadFortunesRows();
    }
}
