namespace LegacyShop.Common
{
    public readonly struct Money
    {
        public Money(decimal amount, string currency) { Amount = amount; Currency = currency; }
        public decimal Amount { get; }
        public string Currency { get; }
    }
}
