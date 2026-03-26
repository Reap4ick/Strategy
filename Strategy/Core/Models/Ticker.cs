namespace ArbitrageBot.Core.Models;

public class Ticker
{
    public decimal Price { get; set; }
    public decimal Ask { get; set; }
    public decimal Bid { get; set; }
    public decimal Volume24h { get; set; }
    public string Url { get; set; } = string.Empty;
}
