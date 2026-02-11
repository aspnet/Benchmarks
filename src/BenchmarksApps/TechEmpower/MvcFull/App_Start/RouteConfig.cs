using System.Web.Mvc;
using System.Web.Routing;

namespace MvcFull
{
    public class RouteConfig
    {
        public static void RegisterRoutes(RouteCollection routes)
        {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");

            // Custom routes for benchmark endpoints
            routes.MapRoute(
                name: "Plaintext",
                url: "plaintext",
                defaults: new { controller = "Home", action = "Plaintext" }
            );

            routes.MapRoute(
                name: "Json",
                url: "json",
                defaults: new { controller = "Home", action = "Json" }
            );

            routes.MapRoute(
                name: "Db",
                url: "db",
                defaults: new { controller = "SingleQuery", action = "Index" }
            );

            routes.MapRoute(
                name: "Queries",
                url: "queries/{count}",
                    defaults: new { controller = "MultipleQueries", action = "Index", count = UrlParameter.Optional }
            );

            routes.MapRoute(
                 name: "Updates",
                url: "updates/{count}",
                defaults: new { controller = "Updates", action = "Index", count = UrlParameter.Optional }
            );

            routes.MapRoute(
                name: "Fortunes",
                url: "fortunes",
                defaults: new { controller = "Fortunes", action = "Index" }
            );

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
