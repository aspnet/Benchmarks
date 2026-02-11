using System.Threading.Tasks;
using System.Web.Mvc;
using MvcFull.Database;
using MvcFull.Models;
using Newtonsoft.Json;

namespace MvcFull.Controllers
{
    public class UpdatesController : Controller
    {
        [Route("updates/{count?}")]
        public async Task<ContentResult> Index(int count = 1)
        {
            using (var dbContext = new ApplicationDbContext())
            {
                var db = new Db(dbContext);
                var result = await db.LoadMultipleUpdatesRows(count);
                var json = JsonConvert.SerializeObject(result);
                return Content(json, "application/json");
            }
        }
    }
}
