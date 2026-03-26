namespace ArbitrageBot.Core.Models;

public class OrderResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public object? Data { get; set; }
}
