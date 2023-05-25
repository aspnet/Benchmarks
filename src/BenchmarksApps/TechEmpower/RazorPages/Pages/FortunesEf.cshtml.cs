using Microsoft.AspNetCore.Mvc.RazorPages;
using RazorPages.Database;
using RazorPages.Models;

namespace RazorPages.Pages;

public class FortunesEfModel(Db db, AppDbContext dbContext) : PageModel
{
    public List<Fortune> Fortunes { get; set; } = null!;

    public async Task OnGetAsync()
    {
        Fortunes = await db.LoadFortunesRowsEf(dbContext);
    }
}
