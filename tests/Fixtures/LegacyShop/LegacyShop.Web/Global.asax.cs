using System;
using System.Web;
using System.Web.Http;

namespace LegacyShop.Web
{
    public class WebApiApplication : HttpApplication
    {
        protected void Application_Start()
        {
            GlobalConfiguration.Configure(WebApiConfig.Register);
        }

        protected void Application_BeginRequest(object sender, EventArgs e)
        {
            HttpContext.Current.Items["RequestStart"] = DateTime.UtcNow;
        }
    }
}
