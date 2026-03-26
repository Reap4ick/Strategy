using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArbitrageBot.Core.Interfaces;
using ArbitrageBot.Core.Models;

namespace ArbitrageBot.Infrastructure.Clients;

public class GateClient : IExchangeClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _secret;
    private readonly string _baseUrl = "https://api.gateio.ws";

    public string Name => "Gate.io";
    public Dictionary<string, dynamic> PrecisionData { get; } = new();

    public GateClient(HttpClient httpClient, string apiKey, string secret)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _secret = secret;
    }

    private Dictionary<string, string> GetSignature(string method, string url, string? queryString = null, string? payloadString = null)
    {
        var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        using var sha512 = SHA512.Create();
        var hashedPayload = BitConverter.ToString(sha512.ComputeHash(Encoding.UTF8.GetBytes(payloadString ?? ""))).Replace("-", "").ToLower();
        var s = $"{method}\n{url}\n{queryString ?? ""}\n{hashedPayload}\n{t}";
        
        var keyBytes = Encoding.UTF8.GetBytes(_secret);
        var sBytes = Encoding.UTF8.GetBytes(s);
        using var hmac = new HMACSHA512(keyBytes);
        var sign = BitConverter.ToString(hmac.ComputeHash(sBytes)).Replace("-", "").ToLower();
        
        return new Dictionary<string, string>
        {
            { "KEY", _apiKey },
            { "Timestamp", t },
            { "SIGN", sign }
        };
    }

    public async Task<List<string>> GetAllSymbolsAsync()
    {
        try
        {
            var url = "/api/v4/futures/usdt/contracts";
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{url}");
            var sig = GetSignature("GET", url);
            foreach (var kvp in sig) request.Headers.Add(kvp.Key, kvp.Value);
            
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var symbols = new List<string>();
                foreach (var item in doc.RootElement.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString()!;
                    PrecisionData[name] = new
                    {
                        Precision = 0,
                        Multiplier = decimal.Parse(item.GetProperty("quanto_multiplier").GetString()!),
                        PricePrecision = (int)Math.Abs(Math.Log10(double.Parse(item.GetProperty("order_price_round").GetString()!)))
                    };
                    symbols.Add(name);
                }
                return symbols;
            }
        }
        catch { }
        return new List<string>();
    }

    public async Task<Ticker?> GetTickerAsync(string symbol)
    {
        try
        {
            var url = $"/api/v4/futures/usdt/tickers?contract={symbol}";
            var response = await _httpClient.GetAsync($"{_baseUrl}{url}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                var d = doc.RootElement[0];
                return new Ticker
                {
                    Price = decimal.Parse(d.GetProperty("last").GetString()!),
                    Ask = decimal.Parse(d.GetProperty("lowest_ask").GetString()!),
                    Bid = decimal.Parse(d.GetProperty("highest_bid").GetString()!),
                    Volume24h = decimal.Parse(d.GetProperty("volume_24_quote").GetString()!),
                    Url = $"https://www.gate.io/futures/usdt/{symbol}"
                };
            }
        }
        catch { }
        return null;
    }

    public async Task<List<Kline>> GetKlinesAsync(string symbol, int limit = 100)
    {
        try
        {
            var url = $"/api/v4/futures/usdt/candlesticks?contract={symbol}&interval=15m&limit={limit}";
            var response = await _httpClient.GetAsync($"{_baseUrl}{url}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                return doc.RootElement.EnumerateArray()
                    .Select(d => new Kline
                    {
                        Time = d.GetProperty("t").GetInt64(),
                        Close = decimal.Parse(d.GetProperty("c").GetString()!)
                    }).ToList();
            }
        }
        catch { }
        return new List<Kline>();
    }

    public async Task<decimal> GetBalanceAsync()
    {
        var url = "/api/v4/futures/usdt/accounts";
        var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}{url}");
        var sig = GetSignature("GET", url);
        foreach (var kvp in sig) request.Headers.Add(kvp.Key, kvp.Value);
        
        try
        {
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            return decimal.Parse(doc.RootElement.GetProperty("available").GetString()!);
        }
        catch { }
        return 0m;
    }

    public async Task<OrderResponse> PlaceOrderAsync(string symbol, string side, decimal quantity, string positionSide, string type = "MARKET", bool close = false)
    {
        var url = "/api/v4/futures/usdt/orders";
        var body = new
        {
            contract = symbol,
            size = close ? 0 : (int)quantity,
            price = "0",
            tif = "ioc"
        };
        var payload = JsonSerializer.Serialize(body);
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}{url}");
        var sig = GetSignature("POST", url, payloadString: payload);
        foreach (var kvp in sig) request.Headers.Add(kvp.Key, kvp.Value);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            if (response.IsSuccessStatusCode)
            {
                return new OrderResponse { Success = true, Data = content };
            }
            return new OrderResponse { Success = false, Message = doc.RootElement.GetProperty("label").GetString() ?? "Unknown error" };
        }
        catch (Exception ex)
        {
            return new OrderResponse { Success = false, Message = ex.Message };
        }
    }
}
