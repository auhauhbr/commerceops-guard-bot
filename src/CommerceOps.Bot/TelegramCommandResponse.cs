using System.Text.Json.Serialization;

namespace CommerceOps.Bot;

public sealed record TelegramCommandResponse(
    string Text,
    IReadOnlyList<IReadOnlyList<TelegramInlineButton>> InlineKeyboard)
{
    public static TelegramCommandResponse TextOnly(string text)
    {
        return new TelegramCommandResponse(text, []);
    }
}

public sealed record TelegramInlineButton(
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("callback_data")] string CallbackData);
