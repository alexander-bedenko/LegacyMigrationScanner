using System.Collections.Generic;
using System.Configuration;
using System.Web;
using System.Web.Http;
using LegacyShop.Data;

namespace LegacyShop.Web.Controllers
{
    public class OrdersController : ApiController
    {
        private readonly string _region = ConfigurationManager.AppSettings["Region"];

        [HttpGet]
        public IEnumerable<string> Get()
        {
            var user = HttpContext.Current.User.Identity.Name;
            var session = HttpContext.Current.Session["cart"];
            using (var db = new ShopContext())
            {
                return new[] { user, _region };
            }
        }

        [HttpPost]
        public IHttpActionResult Post(string id)
        {
            var client = new OrderServiceClient();
            client.PlaceOrder(id);
            return Ok();
        }
    }

    public class OrderServiceClient : System.ServiceModel.ClientBase<IOrderServiceProxy>
    {
        public void PlaceOrder(string id) => Channel.PlaceOrder(id);
    }

    [System.ServiceModel.ServiceContract]
    public interface IOrderServiceProxy
    {
        [System.ServiceModel.OperationContract]
        void PlaceOrder(string id);
    }
}
