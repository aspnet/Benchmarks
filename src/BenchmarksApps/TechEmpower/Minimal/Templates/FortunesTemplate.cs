using System.Globalization;
using System.Text.Encodings.Web;
using Minimal.Models;

namespace Minimal.Templates;

public class FortunesTemplate
{
    public static async Task Render(List<Fortune> fortunes, HttpResponse response)
    {
        var htmlEncoder = HtmlEncoder.Default;

        await response.WriteAsync("<!DOCTYPE html><html><head><title>Fortunes</title></head><body><table><tr><th>id</th><th>message</th></tr>");

        foreach (var item in fortunes)
        {
            await response.WriteAsync("<tr><td>");
            await response.WriteAsync(item.Id.ToString(CultureInfo.InvariantCulture));
            await response.WriteAsync("</td><td>");
            await response.WriteAsync(htmlEncoder.Encode(item.Message));
            await response.WriteAsync("</td></tr>");
        }

        await response.WriteAsync("</table></body></html>");
    }
}