using Microsoft.AspNetCore.Mvc;
using Mvc.Database;
using Mvc.Models;

namespace Mvc.Controllers;

public class SingleQueryController : Controller
{
    [Route("db")]
    [Produces("application/json")]
    public Task<World> Index([FromServices] Db db)
    {
        return db.LoadSingleQueryRow();
    }
}
