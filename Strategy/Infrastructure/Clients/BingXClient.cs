using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArbitrageBot.Core.Interfaces;
using ArbitrageBot.Core.Models;

namespace ArbitrageBot.Infrastructure.Clients;

public class BingXClient : IExchangeClient
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _secret;
    private readonly string _baseUrl = "https://open-api.bingx.com";

    public string Name => "BingX";
    public Dictionary<string, dynamic> PrecisionData { get; } = new();

    public BingXClient(HttpClient httpClient, string apiKey, string secret)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _secret = secret;
    }

    private string GetSignature(string payload)
    {
        var keyBytes = Encoding.UTF8.GetBytes(_secret);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return BitConverter.ToString(hash).Replace("-", "").ToLower();
    }

    public async Task<List<string>> GetAllSymbolsAsync()
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/openApi/swap/v2/quote/contracts");
            request.Headers.Add("X-BX-APIKEY", _apiKey);
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.GetProperty("code").GetInt32() == 0)
                {
                    var symbols = new List<string>();
                    foreach (var item in doc.RootElement.GetProperty("data").EnumerateArray())
                    {
                        var symbol = item.GetProperty("symbol").GetString()!;
                        PrecisionData[symbol] = new
                        {
                            Precision = item.TryGetProperty("quantityPrecision", out var qp) ? qp.GetInt32() : 2,
                            PricePrecision = item.TryGetProperty("pricePrecision", out var pp) ? pp.GetInt32() : 4
                        };
                        if (symbol.Contains("USDT")) symbols.Add(symbol);
                    }
                    return symbols;
                }
            }
        }
        catch { }
        return new List<string>();
    }

    public async Task<Ticker?> GetTickerAsync(string symbol)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/openApi/swap/v2/quote/ticker?symbol={symbol}");
            request.Headers.Add("X-BX-APIKEY", _apiKey);
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.GetProperty("code").GetInt32() == 0)
                {
                    var data = doc.RootElement.GetProperty("data");
                    if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0) data = data[0];
                    return new Ticker
                    {
                        Price = decimal.Parse(data.GetProperty("lastPrice").GetString()!),
                        Ask = decimal.Parse(data.GetProperty("askPrice").GetString()!),
                        Bid = decimal.Parse(data.GetProperty("bidPrice").GetString()!),
                        Volume24h = decimal.Parse(data.GetProperty("volume").GetString()!),
                        Url = $"https://bingx.com/en-us/futures/{symbol}"
                    };
                }
            }
        }
        catch { }
        return null;
    }

    public async Task<List<Kline>> GetKlinesAsync(string symbol, int limit = 100)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{_baseUrl}/openApi/swap/v2/quote/klines?symbol={symbol}&interval=15m&limit={limit}");
            request.Headers.Add("X-BX-APIKEY", _apiKey);
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.GetProperty("code").GetInt32() == 0)
                {
                    return doc.RootElement.GetProperty("data").EnumerateArray()
                        .Select(d => new Kline
                        {
                            Time = d.GetProperty("time").GetInt64() / 1000,
                            Close = decimal.Parse(d.GetProperty("close").GetString()!)
                        }).ToList();
                }
            }
        }
        catch { }
        return new List<Kline>();
    }

    public async Task<decimal> GetBalanceAsync()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var @params = $"timestamp={ts}";
        var sig = GetSignature(@params);
        var url = $"{_baseUrl}/openApi/swap/v2/user/balance?{@params}&signature={sig}";
        
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-BX-APIKEY", _apiKey);
        try
        {
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.GetProperty("code").GetInt32() == 0)
            {
                foreach (var b in doc.RootElement.GetProperty("data").GetProperty("balance").EnumerateArray())
                {
                    if (b.GetProperty("asset").GetString() == "USDT")
                        return decimal.Parse(b.GetProperty("balance").GetString()!);
                }
            }
        }
        catch { }
        return 0m;
    }

    public async Task<OrderResponse> PlaceOrderAsync(string symbol, string side, decimal quantity, string positionSide, string type = "MARKET", bool close = false)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        int prec = PrecisionData.ContainsKey(symbol) ? (int)PrecisionData[symbol].Precision : 2;
        var qtyStr = quantity.ToString($"F{prec}");
        var @params = $"symbol={symbol}&side={side}&positionSide={positionSide}&type={type}&quantity={qtyStr}&timestamp={ts}";
        var sig = GetSignature(@params);
        var url = $"{_baseUrl}/openApi/swap/v2/trade/order?{@params}&signature={sig}";

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-BX-APIKEY", _apiKey);
        try
        {
            var response = await _httpClient.SendAsync(request);
            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.GetProperty("code").GetInt32() == 0)
            {
                return new OrderResponse { Success = true, Data = content };
            }
            return new OrderResponse { Success = false, Message = doc.RootElement.GetProperty("msg").GetString() ?? "Unknown error" };
        }
        catch (Exception ex)
        {
            return new OrderResponse { Success = false, Message = ex.Message };
        }
    }
}
