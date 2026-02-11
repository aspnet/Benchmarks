using System.Threading.Tasks;
using System.Web.Mvc;
using MvcFull.Database;

namespace MvcFull.Controllers
{
    public class FortunesController : Controller
    {
        [Route("fortunes")]
        public async Task<ActionResult> Index()
        {
            using (var dbContext = new ApplicationDbContext())
            {
                var db = new Db(dbContext);
                var fortunes = await db.LoadFortunesRows();
                return View(fortunes);
            }
        }
    }
}
