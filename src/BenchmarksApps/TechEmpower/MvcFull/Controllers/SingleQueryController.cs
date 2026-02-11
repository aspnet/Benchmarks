using System.Threading.Tasks;
using System.Web.Mvc;
using MvcFull.Database;
using MvcFull.Models;
using Newtonsoft.Json;

namespace MvcFull.Controllers
{
    public class SingleQueryController : Controller
    {
        [Route("db")]
        public async Task<ContentResult> Index()
        {
            using (var dbContext = new ApplicationDbContext())
            {
                var db = new Db(dbContext);
                var result = await db.LoadSingleQueryRow();
                var json = JsonConvert.SerializeObject(result);
                return Content(json, "application/json");
            }
        }
    }
}
