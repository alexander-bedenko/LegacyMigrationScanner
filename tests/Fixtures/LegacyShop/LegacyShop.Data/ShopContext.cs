using System.Data.Entity;

namespace LegacyShop.Data
{
    public class ShopContext : DbContext
    {
        public ShopContext() : base("name=ShopDb") { }
        public DbSet<Order> Orders { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
    }
}
