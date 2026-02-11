using System;
using System.Configuration;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace MvcFull
{
    public class MvcApplication : HttpApplication
    {
        public static AppSettings AppSettings { get; private set; }

        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            RouteConfig.RegisterRoutes(RouteTable.Routes);

            // Load configuration
            AppSettings = new AppSettings
            {
                ConnectionString = ConfigurationManager.ConnectionStrings["DefaultConnection"]?.ConnectionString
            };
        }
    }
}
