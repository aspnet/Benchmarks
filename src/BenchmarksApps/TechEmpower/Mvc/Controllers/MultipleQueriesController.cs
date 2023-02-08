using Microsoft.AspNetCore.Mvc;
using Mvc.Database;
using Mvc.Models;

namespace Mvc.Controllers;

public class MultipleQueriesController : Controller
{
    [Route("queries/{count?}")]
    [Produces("application/json")]
    public Task<World[]> Index([FromServices] Db db, int count = 1)
    {
        count = count < 1 ? 1 : count > 500 ? 500 : count;
        return db.LoadMultipleQueriesRows(count);
    }
}
