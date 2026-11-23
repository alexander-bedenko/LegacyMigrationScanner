using Microsoft.Owin;
using Owin;

[assembly: OwinStartup(typeof(LegacyShop.Web.Startup))]

namespace LegacyShop.Web
{
    public class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            app.Use(async (ctx, next) => { await next(); });
        }
    }
}
