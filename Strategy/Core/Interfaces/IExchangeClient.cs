using ArbitrageBot.Core.Models;

namespace ArbitrageBot.Core.Interfaces;

public interface IExchangeClient
{
    string Name { get; }
    Task<List<string>> GetAllSymbolsAsync();
    Task<Ticker?> GetTickerAsync(string symbol);
    Task<List<Kline>> GetKlinesAsync(string symbol, int limit = 100);
    Task<decimal> GetBalanceAsync();
    Task<OrderResponse> PlaceOrderAsync(string symbol, string side, decimal quantity, string positionSide, string type = "MARKET", bool close = false);
    Dictionary<string, dynamic> PrecisionData { get; }
}
