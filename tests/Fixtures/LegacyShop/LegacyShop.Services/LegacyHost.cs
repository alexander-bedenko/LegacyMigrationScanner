using System;
using System.ServiceModel;

namespace LegacyShop.Services
{
    public static class LegacyHost
    {
        public static void Run()
        {
            using (var host = new ServiceHost(typeof(OrderService)))
            {
                host.Open();
            }

            var sandbox = AppDomain.CreateDomain("Plugins");
            AppDomain.Unload(sandbox);
        }
    }
}
