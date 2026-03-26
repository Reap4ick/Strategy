namespace ArbitrageBot.Core.Models;

public class Trade
{
    public string BingXSymbol { get; set; } = string.Empty;
    public string GateSymbol { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public decimal QtyBingX { get; set; }
    public decimal SizeGate { get; set; }
    public string BingXPositionSide { get; set; } = string.Empty;
    public decimal SpreadAtOpen { get; set; }
}
