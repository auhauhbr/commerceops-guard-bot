using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CommerceOps.Bot;

public sealed class TelegramBotService(
    IServiceScopeFactory scopeFactory,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramBotService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient = new();
    private readonly TelegramBotOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            logger.LogWarning("Telegram bot token is not configured. Bot polling is disabled.");
            return;
        }

        var offset = 0L;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var updates = await GetUpdatesAsync(offset, stoppingToken);

                foreach (var update in updates)
                {
                    offset = Math.Max(offset, update.UpdateId + 1);
                    await HandleUpdateAsync(update, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError("Telegram polling failed: {ErrorType}.", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    public override void Dispose()
    {
        _httpClient.Dispose();
        base.Dispose();
    }

    private async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, CancellationToken cancellationToken)
    {
        var url = CreateApiUrl($"getUpdates?offset={offset}&timeout=25&allowed_updates=%5B%22message%22%5D");
        var response = await _httpClient.GetFromJsonAsync<TelegramApiResponse<List<TelegramUpdate>>>(url, JsonOptions, cancellationToken);

        return response?.Ok == true && response.Result is not null ? response.Result : [];
    }

    private async Task HandleUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken)
    {
        if (update.Message?.Text is not { Length: > 0 } text || update.Message.From is null)
        {
            return;
        }

        using var scope = scopeFactory.CreateScope();
        var router = scope.ServiceProvider.GetRequiredService<TelegramCommandRouter>();
        var responseText = await router.RouteAsync(
            new TelegramCommandContext(update.Message.Chat.Id, update.Message.From.Id, text),
            cancellationToken);

        await SendMessageAsync(update.Message.Chat.Id, responseText, cancellationToken);
    }

    private async Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        var request = new TelegramSendMessageRequest(chatId, text);
        var response = await _httpClient.PostAsJsonAsync(CreateApiUrl("sendMessage"), request, JsonOptions, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private string CreateApiUrl(string method)
    {
        return $"https://api.telegram.org/bot{_options.BotToken}/{method}";
    }
}

internal sealed record TelegramApiResponse<T>(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] T? Result);

internal sealed record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message);

internal sealed record TelegramMessage(
    [property: JsonPropertyName("message_id")] long MessageId,
    [property: JsonPropertyName("from")] TelegramUser? From,
    [property: JsonPropertyName("chat")] TelegramChat Chat,
    [property: JsonPropertyName("text")] string? Text);

internal sealed record TelegramUser(
    [property: JsonPropertyName("id")] long Id);

internal sealed record TelegramChat(
    [property: JsonPropertyName("id")] long Id);

internal sealed record TelegramSendMessageRequest(
    [property: JsonPropertyName("chat_id")] long ChatId,
    [property: JsonPropertyName("text")] string Text);
