namespace SimpleText.Core.Updates;

/// <summary>A single downloadable file attached to a GitHub release.</summary>
public sealed record ReleaseAsset(string Name, string DownloadUrl);
