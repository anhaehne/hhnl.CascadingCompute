namespace TestProject.Client.Contracts;

public sealed record CacheInvalidationMessage(
    string CacheKey,
    IReadOnlyDictionary<string, string> Taints,
    long SequenceId,
    DateTimeOffset UtcTimestamp);
