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
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
                    try
                    {
                        await HandleUpdateAsync(update, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        logger.LogError(
                            ex,
                            "Telegram update processing failed. UpdateId: {UpdateId}; UpdateType: {UpdateType}; Command: {Command}.",
                            update.UpdateId,
                            GetUpdateType(update),
                            GetCommand(update));
                        throw;
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Telegram polling failed.");
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
        var url = CreateApiUrl($"getUpdates?offset={offset}&timeout=25&allowed_updates=%5B%22message%22,%22callback_query%22%5D");
        var response = await _httpClient.GetFromJsonAsync<TelegramApiResponse<List<TelegramUpdate>>>(url, JsonOptions, cancellationToken);

        return response?.Ok == true && response.Result is not null ? response.Result : [];
    }

    private async Task HandleUpdateAsync(TelegramUpdate update, CancellationToken cancellationToken)
    {
        if (update.Message?.Text is { Length: > 0 } text && update.Message.From is not null)
        {
            await HandleMessageAsync(update.Message, text, cancellationToken);
            return;
        }

        if (update.CallbackQuery?.Data is { Length: > 0 } callbackData && update.CallbackQuery.From is not null)
        {
            await HandleCallbackQueryAsync(update.CallbackQuery, callbackData, cancellationToken);
        }
    }

    private async Task HandleMessageAsync(
        TelegramMessage message,
        string text,
        CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var router = scope.ServiceProvider.GetRequiredService<TelegramCommandRouter>();
            var context = new TelegramCommandContext(message.Chat.Id, message.From!.Id, text);
            var preliminaryMessage = router.GetPreliminaryMessage(context);
            if (preliminaryMessage is not null)
            {
                await SendMessageAsync(message.Chat.Id, TelegramCommandResponse.TextOnly(preliminaryMessage), cancellationToken);
            }

            var response = await router.RouteMessageAsync(context, cancellationToken);
            await SendMessageAsync(message.Chat.Id, response, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Telegram message processing failed. UpdateType: message; Command: {Command}.",
                GetCommand(text));
            throw;
        }
    }

    private async Task HandleCallbackQueryAsync(
        TelegramCallbackQuery callbackQuery,
        string callbackData,
        CancellationToken cancellationToken)
    {
        try
        {
            await AnswerCallbackQueryAsync(callbackQuery.Id, cancellationToken);

            using var scope = scopeFactory.CreateScope();
            var router = scope.ServiceProvider.GetRequiredService<TelegramCommandRouter>();
            var chatId = callbackQuery.Message?.Chat.Id;
            if (chatId is null)
            {
                return;
            }

            var preliminaryMessage = router.GetCallbackPreliminaryMessage(callbackData);
            if (preliminaryMessage is not null)
            {
                await SendMessageAsync(chatId.Value, TelegramCommandResponse.TextOnly(preliminaryMessage), cancellationToken);
            }

            var response = await router.RouteCallbackAsync(
                callbackData,
                chatId.Value,
                callbackQuery.From.Id,
                cancellationToken);

            await SendMessageAsync(chatId.Value, response, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Telegram callback query processing failed. UpdateType: callback_query; CallbackAction: {CallbackAction}.",
                GetCallbackAction(callbackData));
            throw;
        }
    }

    private async Task SendMessageAsync(
        long chatId,
        TelegramCommandResponse commandResponse,
        CancellationToken cancellationToken)
    {
        var replyMarkup = commandResponse.InlineKeyboard.Count == 0
            ? null
            : new TelegramInlineKeyboardMarkup(commandResponse.InlineKeyboard);
        var buttonCount = CountInlineKeyboardButtons(commandResponse.InlineKeyboard);

        logger.LogInformation(
            "Telegram sendMessage payload. ChatId: {ChatId}; TextLength: {TextLength}; HasReplyMarkup: {HasReplyMarkup}; ButtonCount: {ButtonCount}.",
            chatId,
            commandResponse.Text.Length,
            replyMarkup is not null,
            buttonCount);

        try
        {
            var request = new TelegramSendMessageRequest(chatId, commandResponse.Text, replyMarkup);
            using var response = await _httpClient.PostAsJsonAsync(CreateApiUrl("sendMessage"), request, JsonOptions, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogError(
                "Telegram sendMessage failed. StatusCode: {StatusCode}; ResponseBody: {ResponseBody}",
                (int)response.StatusCode,
                responseBody);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex,
                "Telegram sendMessage request failed. ChatId: {ChatId}; TextLength: {TextLength}; HasReplyMarkup: {HasReplyMarkup}; ButtonCount: {ButtonCount}.",
                chatId,
                commandResponse.Text.Length,
                replyMarkup is not null,
                buttonCount);
        }
    }

    private async Task AnswerCallbackQueryAsync(string callbackQueryId, CancellationToken cancellationToken)
    {
        try
        {
            var request = new TelegramAnswerCallbackQueryRequest(callbackQueryId);
            var response = await _httpClient.PostAsJsonAsync(CreateApiUrl("answerCallbackQuery"), request, JsonOptions, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Telegram answerCallbackQuery failed.");
            throw;
        }
    }

    private string CreateApiUrl(string method)
    {
        return $"https://api.telegram.org/bot{_options.BotToken}/{method}";
    }

    private static string GetUpdateType(TelegramUpdate update)
    {
        if (update.Message is not null)
        {
            return "message";
        }

        if (update.CallbackQuery is not null)
        {
            return "callback_query";
        }

        return "unknown";
    }

    private static string? GetCommand(TelegramUpdate update)
    {
        return update.Message?.Text is { Length: > 0 } text ? GetCommand(text) : null;
    }

    private static string? GetCommand(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return null;
        }

        return trimmed.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
    }

    private static string GetCallbackAction(string callbackData)
    {
        var parts = callbackData.Split(':', 3, StringSplitOptions.TrimEntries);
        return parts.Length >= 2 ? $"{parts[0]}:{parts[1]}" : "unknown";
    }

    private static int CountInlineKeyboardButtons(IReadOnlyList<IReadOnlyList<TelegramInlineButton>> inlineKeyboard)
    {
        var count = 0;
        foreach (var row in inlineKeyboard)
        {
            count += row.Count;
        }

        return count;
    }
}

internal sealed record TelegramApiResponse<T>(
    [property: JsonPropertyName("ok")] bool Ok,
    [property: JsonPropertyName("result")] T? Result);

internal sealed record TelegramUpdate(
    [property: JsonPropertyName("update_id")] long UpdateId,
    [property: JsonPropertyName("message")] TelegramMessage? Message,
    [property: JsonPropertyName("callback_query")] TelegramCallbackQuery? CallbackQuery);

internal sealed record TelegramCallbackQuery(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("from")] TelegramUser From,
    [property: JsonPropertyName("message")] TelegramMessage? Message,
    [property: JsonPropertyName("data")] string? Data);

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
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("reply_markup")] TelegramInlineKeyboardMarkup? ReplyMarkup);

internal sealed record TelegramInlineKeyboardMarkup(
    [property: JsonPropertyName("inline_keyboard")] IReadOnlyList<IReadOnlyList<TelegramInlineButton>> InlineKeyboard);

internal sealed record TelegramAnswerCallbackQueryRequest(
    [property: JsonPropertyName("callback_query_id")] string CallbackQueryId);
