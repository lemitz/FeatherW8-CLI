namespace FeatherW8.Core;

internal sealed record QueueTrack(string Path, string? Title = null, int? Number = null, CueRange? Range = null);

// CUE timestamps are CD frames (75 per second), not audio sample frames.
internal sealed record CueRange(long StartFrames, long? EndFrames);
