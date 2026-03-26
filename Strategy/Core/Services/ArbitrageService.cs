using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ArbitrageBot.Core.Interfaces;
using ArbitrageBot.Core.Models;
using Microsoft.Extensions.Logging;

namespace ArbitrageBot.Core.Services;

public class ArbitrageService
{
    private readonly ILogger<ArbitrageService> _logger;
    private readonly IExchangeClient _bingx;
    private readonly IExchangeClient _gate;
    private readonly ConcurrentDictionary<string, Trade> _activeTrades = new();
    private readonly ConcurrentDictionary<string, DateTime> _blacklist = new();
    
    // Strategy Settings (should be moved to configuration)
    private readonly decimal _thresholdPercent = 3.0m;
    private readonly decimal _maxSpreadThreshold = 15.0m;
    private readonly decimal _closeThreshold = 0.0m;
    private readonly int _intersectionThreshold = 5;
    private readonly decimal _tradeAmountUsdt = 5.0m;
    private readonly int _blacklistDurationSeconds = 3600;

    public ArbitrageService(ILogger<ArbitrageService> logger, IExchangeClient bingx, IExchangeClient gate)
    {
        _logger = logger;
        _bingx = bingx;
        _gate = gate;
    }

    private string NormalizeSymbol(string s)
    {
        s = s.ToUpper().Replace("USDT", "").Replace("-", "").Replace("_", "");
        return Regex.Replace(s, @"[^A-Z0-9]", "");
    }

    private void AddToBlacklist(string symbol)
    {
        _blacklist[symbol] = DateTime.UtcNow.AddSeconds(_blacklistDurationSeconds);
        _logger.LogInformation("Added {Symbol} to blacklist for {Duration}s", symbol, _blacklistDurationSeconds);
    }

    private bool IsBlacklisted(string symbol)
    {
        if (_blacklist.TryGetValue(symbol, out var expiry))
        {
            if (DateTime.UtcNow < expiry) return true;
            _blacklist.TryRemove(symbol, out _);
        }
        return false;
    }

    public async Task RunMonitorAsync(CancellationToken ct)
    {
        List<(string BingX, string Gate)> commonPairs = new();
        DateTime lastUpdate = DateTime.MinValue;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if ((DateTime.UtcNow - lastUpdate).TotalSeconds > 3600)
                {
                    var bSyms = await _bingx.GetAllSymbolsAsync();
                    var gSyms = await _gate.GetAllSymbolsAsync();
                    var gMap = gSyms.ToDictionary(NormalizeSymbol, s => s);
                    commonPairs = bSyms.Where(bs => gMap.ContainsKey(NormalizeSymbol(bs)))
                                       .Select(bs => (bs, gMap[NormalizeSymbol(bs)]))
                                       .ToList();
                    lastUpdate = DateTime.UtcNow;
                    _logger.LogInformation("Updated symbols. Common pairs: {Count}", commonPairs.Count);
                }

                if (_activeTrades.Any())
                {
                    var activeBSym = _activeTrades.Keys.First();
                    var trade = _activeTrades[activeBSym];
                    var bTick = await _bingx.GetTickerAsync(activeBSym);
                    var gTick = await _gate.GetTickerAsync(trade.GateSymbol);

                    if (bTick != null && gTick != null)
                    {
                        decimal currentSpread = (bTick.Price - gTick.Price) / gTick.Price * 100;
                        bool shouldClose = (trade.SpreadAtOpen > 0 && currentSpread <= _closeThreshold) ||
                                           (trade.SpreadAtOpen < 0 && currentSpread >= -_closeThreshold);
                        if (shouldClose)
                        {
                            await CloseArbitrageAsync(activeBSym, trade, currentSpread);
                        }
                    }
                    await Task.Delay(20000, ct);
                    continue;
                }

                foreach (var pair in commonPairs)
                {
                    if (IsBlacklisted(pair.BingX)) continue;

                    var bTickTask = _bingx.GetTickerAsync(pair.BingX);
                    var gTickTask = _gate.GetTickerAsync(pair.Gate);
                    await Task.WhenAll(bTickTask, gTickTask);

                    var bTick = bTickTask.Result;
                    var gTick = gTickTask.Result;

                    if (bTick != null && gTick != null)
                    {
                        decimal spread = (bTick.Price - gTick.Price) / gTick.Price * 100;
                        if (Math.Abs(spread) >= _thresholdPercent && Math.Abs(spread) <= _maxSpreadThreshold)
                        {
                            var (intersections, _, _) = await CheckIntersectionsAsync(pair.BingX, pair.Gate);
                            if (intersections >= _intersectionThreshold)
                            {
                                await ExecuteArbitrageAsync(pair.BingX, pair.Gate, bTick, gTick, spread);
                                break;
                            }
                        }
                    }
                    await Task.Delay(100, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Loop error");
            }
            await Task.Delay(20000, ct);
        }
    }

    private async Task<(int Intersections, List<Kline> K1, List<Kline> K2)> CheckIntersectionsAsync(string bSym, string gSym)
    {
        var k1Task = _bingx.GetKlinesAsync(bSym);
        var k2Task = _gate.GetKlinesAsync(gSym);
        await Task.WhenAll(k1Task, k2Task);

        var k1 = k1Task.Result;
        var k2 = k2Task.Result;

        if (!k1.Any() || !k2.Any()) return (0, k1, k2);

        var d1 = k1.ToDictionary(k => k.Time, k => k.Close);
        var d2 = k2.ToDictionary(k => k.Time, k => k.Close);
        var common = d1.Keys.Intersect(d2.Keys).OrderBy(t => t).ToList();

        if (common.Count < 2) return (0, k1, k2);

        int intersections = 0;
        decimal? lastDiff = null;

        foreach (var t in common)
        {
            decimal diff = d1[t] - d2[t];
            if (lastDiff.HasValue && ((diff >= 0 && lastDiff < 0) || (diff <= 0 && lastDiff > 0)))
            {
                intersections++;
            }
            lastDiff = diff;
        }

        return (intersections, k1, k2);
    }

    private async Task ExecuteArbitrageAsync(string bSym, string gSym, Ticker bTick, Ticker gTick, decimal spread)
    {
        _logger.LogInformation("Attempting arbitrage for {BSym}/{GSym} (Spread: {Spread:F2}%)", bSym, gSym, spread);

        decimal gMult = _gate.PrecisionData.ContainsKey(gSym) ? (decimal)_gate.PrecisionData[gSym].Multiplier : 1.0m;
        decimal qtyCoins = (_tradeAmountUsdt * 0.98m) / bTick.Price;
        int gSize = (int)(qtyCoins / gMult);
        if (gSize == 0) gSize = 1;

        decimal finalQtyBingX = gSize * gMult;
        int finalSizeGate = gSize;

        string bSide, bPos;
        int gSideVal;

        if (spread > 0)
        {
            bSide = "SELL"; bPos = "SHORT"; gSideVal = finalSizeGate;
        }
        else
        {
            bSide = "BUY"; bPos = "LONG"; gSideVal = -finalSizeGate;
        }

        var bRes = await _bingx.PlaceOrderAsync(bSym, bSide, finalQtyBingX, bPos);
        if (bRes.Success)
        {
            var gRes = await _gate.PlaceOrderAsync(gSym, "", gSideVal, "");
            if (!gRes.Success)
            {
                _logger.LogError("Gate failed. Rolling back BingX. Error: {Error}", gRes.Message);
                string rollbackSide = bSide == "SELL" ? "BUY" : "SELL";
                await _bingx.PlaceOrderAsync(bSym, rollbackSide, finalQtyBingX, bPos);
                AddToBlacklist(bSym);
                return;
            }

            _activeTrades[bSym] = new Trade
            {
                BingXSymbol = bSym,
                GateSymbol = gSym,
                StartTime = DateTime.UtcNow,
                QtyBingX = finalQtyBingX,
                SizeGate = finalSizeGate,
                BingXPositionSide = bPos,
                SpreadAtOpen = spread
            };
            _logger.LogInformation("Arbitrage opened for {BSym}/{GSym}", bSym, gSym);
        }
        else
        {
            AddToBlacklist(bSym);
        }
    }

    private async Task CloseArbitrageAsync(string bSym, Trade trade, decimal spread)
    {
        _logger.LogInformation("Closing {BSym}/{GSym} at Spread: {Spread:F2}%", bSym, trade.GateSymbol, spread);

        string closeSideB = trade.BingXPositionSide == "SHORT" ? "BUY" : "SELL";
        var bRes = await _bingx.PlaceOrderAsync(bSym, closeSideB, trade.QtyBingX, trade.BingXPositionSide);
        var gRes = await _gate.PlaceOrderAsync(trade.GateSymbol, "", 0, "", close: true);

        if (bRes.Success || gRes.Success)
        {
            var duration = (DateTime.UtcNow - trade.StartTime).TotalMinutes;
            _logger.LogInformation("Arbitrage closed for {BSym}/{GSym}. Duration: {Duration:F1} min", bSym, trade.GateSymbol, duration);
            _activeTrades.TryRemove(bSym, out _);
        }
    }
}
