namespace CommerceOps.Infrastructure.Lumora;

public sealed class LumoraOptions
{
    public const string SectionName = "Lumora";

    public string AppId { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string SharedSecret { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;
}
