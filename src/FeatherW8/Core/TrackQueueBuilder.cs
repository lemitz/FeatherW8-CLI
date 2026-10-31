namespace FeatherW8.Core;

internal static class TrackQueueBuilder
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".opus", ".flac", ".fla", ".dsf", ".dff"
    };
    private static readonly HashSet<string> CueAudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".flac", ".fla"
    };

    public static List<QueueTrack> Build(IEnumerable<string> arguments, TextWriter diagnostics)
    {
        var tracks = new List<QueueTrack>();

        foreach (string argument in arguments)
        {
            string path = Environment.ExpandEnvironmentVariables(argument.Trim('"'));

            if (Directory.Exists(path))
            {
                try
                {
                    string[] files = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                        .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var cues = new Dictionary<string, List<QueueTrack>>(StringComparer.OrdinalIgnoreCase);
                    var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // Parse all sheets first so a raw file preceding its sheet by name is not queued twice.
                    foreach (string file in files.Where(IsCue))
                    {
                        List<QueueTrack> cueTracks = CueSheetParser.Parse(file, diagnostics);
                        cues[file] = cueTracks;
                        foreach (QueueTrack track in cueTracks)
                            referenced.Add(track.Path);
                    }

                    foreach (string file in files)
                    {
                        if (IsCue(file))
                            tracks.AddRange(cues[file]);
                        else if (IsSupported(file) && !referenced.Contains(Path.GetFullPath(file)))
                            tracks.Add(new QueueTrack(Path.GetFullPath(file)));
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                    or NotSupportedException or System.Security.SecurityException)
                {
                    diagnostics.WriteLine(Texts.Format("FolderReadError", path, ex.Message));
                }

                continue;
            }

            if (!File.Exists(path))
            {
                diagnostics.WriteLine(Texts.Format("NotFound", path));
                continue;
            }

            if (IsCue(path))
            {
                tracks.AddRange(CueSheetParser.Parse(path, diagnostics));
                continue;
            }

            if (!IsSupported(path))
            {
                diagnostics.WriteLine(Texts.Format("UnsupportedFile", path));
                continue;
            }

            try
            {
                tracks.Add(new QueueTrack(Path.GetFullPath(path)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or NotSupportedException or System.Security.SecurityException)
            {
                diagnostics.WriteLine(Texts.Format("NotFound", path));
            }
        }

        return tracks;
    }

    internal static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));

    // A CUE index is a position in PCM samples; never wrap DSD or lossy/variable-rate sources.
    internal static bool IsCueAudioSupported(string path) => CueAudioExtensions.Contains(Path.GetExtension(path));

    private static bool IsCue(string path) => Path.GetExtension(path).Equals(".cue", StringComparison.OrdinalIgnoreCase);
}
