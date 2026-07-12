namespace CommerceOps.Worker;

public sealed class TriageRefreshOptions
{
    public const string SectionName = "TriageRefresh";

    public bool Enabled { get; set; }

    public int IntervalSeconds { get; set; } = 300;

    public int LookbackMinutes { get; set; } = 240;

    public int Limit { get; set; } = 100;

    public string ClientApplicationPublicId { get; set; } = "lumora";
}

