using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ArbitrageBot.Core.Interfaces;
using ArbitrageBot.Core.Services;
using ArbitrageBot.Infrastructure.Clients;
using ArbitrageBot.Infrastructure.Telegram;

namespace ArbitrageBot.API;

public class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Configuration (Replace with your actual keys or use appsettings.json)
                var bingxApiKey = "dBwjPTQymvkEoZMjdBGQhFT5HNWlFZS3KtKOspiDCrmawTDwK7gZak0RyzonKr2SY05OCOATKv9gATiHyw";
                var bingxSecret = "sWgO8yNKj3Qw7JiVGBIty5DHssOSQ0wxPk1RaA1oZ6v8FckkwwEa44Pm65grQ1JWLrR0L6LX2jCTMsBaD8ssQ";
                var gateApiKey = "255d7123c019d758c2d62fad25213cf6";
                var gateSecret = "a2307c5d526d646bb5edf8da8bdadb1867c991210227ebe1d8c5d0b64fac4c37";
                var tgToken = "7923659186:AAHeWV9UDBRhYm71ZU1HVjzckV-YG-7O6P4";
                var tgChatId = 1578680626L;

                // Infrastructure - Clients
                services.AddHttpClient<BingXClient>();
                services.AddHttpClient<GateClient>();

                services.AddSingleton<IExchangeClient>(sp =>
                    new BingXClient(sp.GetRequiredService<HttpClient>(), bingxApiKey, bingxSecret));
                services.AddSingleton<IExchangeClient>(sp =>
                    new GateClient(sp.GetRequiredService<HttpClient>(), gateApiKey, gateSecret));

                // Infrastructure - Telegram
                services.AddSingleton<TelegramBotService>(sp =>
                    new TelegramBotService(sp.GetRequiredService<ILogger<TelegramBotService>>(), tgToken, tgChatId));

                // Core - Services
                services.AddSingleton<ArbitrageService>(sp =>
                {
                    var clients = sp.GetServices<IExchangeClient>();
                    return new ArbitrageService(
                        sp.GetRequiredService<ILogger<ArbitrageService>>(),
                        clients.First(c => c.Name == "BingX"),
                        clients.First(c => c.Name == "Gate.io")
                    );
                });
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Arbitrage Bot Started (BingX & Gate.io)...");

        var arbitrageService = host.Services.GetRequiredService<ArbitrageService>();

        // Use a CancellationToken to allow graceful shutdown
        using var cts = new CancellationTokenSource();
        await arbitrageService.RunMonitorAsync(cts.Token);
    }
}
