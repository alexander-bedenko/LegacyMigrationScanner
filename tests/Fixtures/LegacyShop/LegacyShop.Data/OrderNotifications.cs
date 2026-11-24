using System.Messaging;
using System.Web;

namespace LegacyShop.Data
{
    // Deep-mode showcase (ADR 0005): MSMQ has no modern .NET implementation and no syntax rule;
    // HttpUtility is a System.Web type that still exists on modern .NET (a syntax-mode false positive).
    public class OrderNotifications
    {
        private readonly MessageQueue _queue = new MessageQueue(@".\private$\orders");

        public void Publish(int orderId, string customerName)
        {
            var safeName = HttpUtility.HtmlEncode(customerName);
            _queue.Send(new Message("Order " + orderId + " for " + safeName));
        }
    }
}
