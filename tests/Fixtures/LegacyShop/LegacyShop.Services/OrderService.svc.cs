using System.ServiceModel;
using System.ServiceModel.Activation;

namespace LegacyShop.Services
{
    [AspNetCompatibilityRequirements(RequirementsMode = AspNetCompatibilityRequirementsMode.Allowed)]
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.PerCall)]
    public class OrderService : IOrderService
    {
        public OrderDto GetOrder(int id)
        {
            var ip = System.Web.HttpContext.Current.Request.UserHostAddress;
            return new OrderDto { Id = id };
        }

        public void PlaceOrder(string id) { }
    }
}
