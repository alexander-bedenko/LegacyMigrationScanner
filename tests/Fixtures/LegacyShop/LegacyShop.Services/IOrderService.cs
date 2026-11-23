using System.Runtime.Serialization;
using System.ServiceModel;

namespace LegacyShop.Services
{
    [ServiceContract]
    public interface IOrderService
    {
        [OperationContract]
        OrderDto GetOrder(int id);

        [OperationContract]
        void PlaceOrder(string id);
    }

    [ServiceContract]
    public interface IInventoryService
    {
        [OperationContract]
        int GetStock(string sku);
    }

    [DataContract]
    public class OrderDto
    {
        [DataMember] public int Id { get; set; }
    }
}
