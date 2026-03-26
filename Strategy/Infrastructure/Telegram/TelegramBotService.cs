using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Microsoft.Extensions.Logging;

namespace ArbitrageBot.Infrastructure.Telegram;

public class TelegramBotService
{
    private readonly ITelegramBotClient _botClient;
    private readonly long _chatId;
    private readonly ILogger<TelegramBotService> _logger;

    public TelegramBotService(ILogger<TelegramBotService> logger, string token, long chatId)
    {
        _logger = logger;
        _botClient = new TelegramBotClient(token);
        _chatId = chatId;
    }

    public async Task SendMessageAsync(string message)
    {
        try
        {
            await _botClient.SendMessage(_chatId, message, parseMode: ParseMode.Markdown);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending Telegram message");
        }
    }
}
