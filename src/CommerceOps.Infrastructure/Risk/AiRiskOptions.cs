namespace CommerceOps.Infrastructure.Risk;

public sealed class AiRiskOptions
{
    public const string SectionName = "AiRisk";
    public const string OpenAiProvider = "openai";

    public bool Enabled { get; set; }
    public int TimeoutSeconds { get; set; } = 3;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public string? ApiKey { get; set; }
}
