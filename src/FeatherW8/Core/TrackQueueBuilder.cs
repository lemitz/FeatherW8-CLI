namespace FeatherW8.Core;

internal static class TrackQueueBuilder
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".ogg", ".opus", ".flac", ".fla", ".dsf", ".dff"
    };

    public static List<string> Build(IEnumerable<string> arguments, TextWriter diagnostics)
    {
        var tracks = new List<string>();

        foreach (string argument in arguments)
        {
            string path = Environment.ExpandEnvironmentVariables(argument.Trim('"'));

            if (Directory.Exists(path))
            {
                try
                {
                    var fromFolder = Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly)
                        .Where(IsSupported)
                        .OrderBy(file => Path.GetFileName(file), StringComparer.OrdinalIgnoreCase);

                    tracks.AddRange(fromFolder);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
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

            if (!IsSupported(path))
            {
                diagnostics.WriteLine(Texts.Format("UnsupportedFile", path));
                continue;
            }

            tracks.Add(Path.GetFullPath(path));
        }

        return tracks;
    }

    private static bool IsSupported(string path) => SupportedExtensions.Contains(Path.GetExtension(path));
}
