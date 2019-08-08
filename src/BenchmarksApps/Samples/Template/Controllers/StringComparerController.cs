using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace Template.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StringComparerController : ControllerBase
    {
        [HttpGet]
        [Route("InvariantCultureIgnoreCase/{count}")]
        public ActionResult InvariantCultureIgnoreCase(int count)
        {
            var data = new Dictionary<string, int>(StringComparer.InvariantCultureIgnoreCase);
            for (var i = 0; i < count; i++)
            {
                data.TryAdd("Id_", i);
            }

            return Ok();
        }
    }
}
