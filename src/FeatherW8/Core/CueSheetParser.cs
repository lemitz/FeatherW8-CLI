using System.Globalization;
using System.Text;
using FeatherW8.Audio;
using NAudio.Wave;

namespace FeatherW8.Core;

internal static class CueSheetParser
{
    private sealed class CueTrack
    {
        public required string Path { get; init; }
        public required int Number { get; init; }
        public required bool IsAudio { get; init; }
        public string? Title { get; set; }
        public long? StartFrames { get; set; }
        public int LastIndex { get; set; } = -1;
    }

    // Parsing is all-or-nothing: an invalid CUE must not hide its raw audio file in a folder scan.
    public static List<QueueTrack> Parse(string cuePath, TextWriter diagnostics)
    {
        try
        {
            string fullPath = Path.GetFullPath(cuePath);
            string contents = ReadCue(fullPath);
            return ParseContents(fullPath, contents, diagnostics);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or System.Security.SecurityException or FormatException)
        {
            diagnostics.WriteLine(Texts.Format("CueParseError", cuePath, ex.Message));
            return new List<QueueTrack>();
        }
    }

    private static string ReadCue(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (bytes.AsSpan().StartsWith(Encoding.Unicode.Preamble))
            return Encoding.Unicode.GetString(bytes, Encoding.Unicode.Preamble.Length, bytes.Length - Encoding.Unicode.Preamble.Length);
        if (bytes.AsSpan().StartsWith(Encoding.BigEndianUnicode.Preamble))
            return Encoding.BigEndianUnicode.GetString(bytes, Encoding.BigEndianUnicode.Preamble.Length, bytes.Length - Encoding.BigEndianUnicode.Preamble.Length);

        int offset = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
        }
        catch (DecoderFallbackException) when (offset == 0)
        {
            // Legacy CUE sheets commonly use the machine's Windows ANSI code page.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage).GetString(bytes);
        }
    }

    private static List<QueueTrack> ParseContents(string cuePath, string contents, TextWriter diagnostics)
    {
        var tracks = new List<CueTrack>();
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lastIndexFrames = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        string? currentFile = null;
        CueTrack? current = null;
        int lineNumber = 0;

        using var reader = new StringReader(contents);
        while (reader.ReadLine() is string line)
        {
            lineNumber++;
            string input = line.Trim();
            if (input.Length == 0)
                continue;

            string command = FirstWord(input, out string value);
            switch (command.ToUpperInvariant())
            {
                case "FILE":
                    string fileName = FileName(value, lineNumber);
                    // CUE paths use Windows separators even when parsed on another platform.
                    fileName = fileName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                    try
                    {
                        currentFile = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cuePath)!, fileName));
                    }
                    catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
                    {
                        throw Invalid(lineNumber, $"invalid FILE path: {ex.Message}");
                    }
                    references.Add(currentFile);
                    current = null;
                    break;

                case "TRACK":
                    if (currentFile is null)
                        throw Invalid(lineNumber, "TRACK appears before FILE");
                    string numberText = FirstWord(value, out string typeText);
                    string type = FirstWord(typeText, out _);
                    if (!int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                        || number is < 1 or > 99 || type.Length == 0)
                        throw Invalid(lineNumber, "invalid TRACK number or type");
                    if (tracks.Count > 0 && number <= tracks[^1].Number)
                        throw Invalid(lineNumber, "TRACK numbers must increase without duplicates");
                    current = new CueTrack
                    {
                        Path = currentFile,
                        Number = number,
                        IsAudio = type.Equals("AUDIO", StringComparison.OrdinalIgnoreCase)
                    };
                    tracks.Add(current);
                    break;

                case "TITLE":
                    if (current is not null)
                        current.Title = TextValue(value, lineNumber);
                    break;

                case "INDEX":
                    if (current is null)
                        throw Invalid(lineNumber, "INDEX appears before TRACK");
                    string[] parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length != 2
                        || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int index)
                        || index is < 0 or > 99)
                        throw Invalid(lineNumber, "invalid INDEX number or timestamp");
                    if (index <= current.LastIndex)
                        throw Invalid(lineNumber, "INDEX numbers must increase without duplicates");
                    long frames = ParseFrames(parts[1], lineNumber);
                    if (lastIndexFrames.TryGetValue(current.Path, out long previous) && frames < previous)
                        throw Invalid(lineNumber, "INDEX timestamps must increase within a FILE");
                    lastIndexFrames[current.Path] = frames;
                    current.LastIndex = index;
                    if (index == 1)
                        current.StartFrames = frames;
                    break;
            }
        }

        if (tracks.Count == 0)
            throw new FormatException("no TRACK entries found");

        var previousStarts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (CueTrack track in tracks)
        {
            if (track.IsAudio && track.StartFrames is null)
                throw new FormatException($"TRACK {track.Number:00} has no INDEX 01");
            if (track.StartFrames is long start)
            {
                if (previousStarts.TryGetValue(track.Path, out long previous) && start <= previous)
                    throw new FormatException($"TRACK {track.Number:00} has a duplicate or reversed INDEX 01");
                previousStarts[track.Path] = start;
            }
        }

        foreach (string reference in references)
        {
            if (!File.Exists(reference))
                throw new FormatException($"referenced file not found: \"{reference}\"");
        }

        var result = new List<QueueTrack>();
        for (int i = 0; i < tracks.Count; i++)
        {
            CueTrack track = tracks[i];
            if (!track.IsAudio)
                continue;
            if (!TrackQueueBuilder.IsCueAudioSupported(track.Path))
            {
                diagnostics.WriteLine(Texts.Format("UnsupportedFile", track.Path));
                continue;
            }

            long? endFrames = null;
            for (int j = i + 1; j < tracks.Count; j++)
            {
                CueTrack next = tracks[j];
                if (string.Equals(next.Path, track.Path, StringComparison.OrdinalIgnoreCase) && next.StartFrames is long start)
                {
                    endFrames = start;
                    break;
                }
            }
            if (endFrames is long end && end <= track.StartFrames!.Value)
                throw new FormatException($"TRACK {track.Number:00} has a duplicate or reversed INDEX 01");
            result.Add(new QueueTrack(track.Path, track.Title, track.Number,
                new CueRange(track.StartFrames!.Value, endFrames)));
        }

        // A syntactically valid sheet can still point past the end of its image. Check before
        // suppressing the raw file in a folder queue, so a bad CUE cannot hide playable audio.
        foreach (IGrouping<string, QueueTrack> fileTracks in result.GroupBy(track => track.Path, StringComparer.OrdinalIgnoreCase))
        {
            using WaveStream source = AudioSourceFactory.Open(fileTracks.Key);
            long totalSamples = source.Length / source.WaveFormat.BlockAlign;
            int sampleRate = source.WaveFormat.SampleRate;
            foreach (QueueTrack queued in fileTracks)
            {
                CueRange range = queued.Range!;
                long start, end;
                try
                {
                    start = checked(range.StartFrames * sampleRate) / 75;
                    end = range.EndFrames is long endFrames
                        ? checked(endFrames * sampleRate) / 75
                        : totalSamples;
                }
                catch (OverflowException)
                {
                    throw new FormatException($"TRACK {queued.Number:00} has an INDEX timestamp that is too large");
                }
                if (start >= totalSamples || end <= start || end > totalSamples)
                    throw new FormatException($"TRACK {queued.Number:00} has an INDEX outside its audio file");
            }
        }

        return result;
    }

    private static string FileName(string value, int line)
    {
        if (value.StartsWith('"'))
        {
            string name = QuotedValue(value, line, out string rest);
            if (name.Length == 0 || FirstWord(rest, out _).Length == 0)
                throw Invalid(line, "FILE requires a name and a type");
            return name;
        }

        int split = value.LastIndexOfAny(' ', '\t');
        if (split < 1 || value[..split].Trim().Length == 0 || value[(split + 1)..].Trim().Length == 0)
            throw Invalid(line, "FILE requires a name and a type");
        return value[..split].Trim();
    }

    private static string TextValue(string value, int line) =>
        value.StartsWith('"') ? QuotedValue(value, line, out _) : value;

    private static string QuotedValue(string value, int line, out string rest)
    {
        var text = new StringBuilder();
        for (int i = 1; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] == '"')
            {
                text.Append('"');
                i++;
            }
            else if (value[i] == '"')
            {
                if (i + 1 < value.Length && value[i + 1] == '"')
                {
                    text.Append('"');
                    i++;
                }
                else
                {
                    rest = value[(i + 1)..].Trim();
                    return text.ToString();
                }
            }
            else
            {
                text.Append(value[i]);
            }
        }
        throw Invalid(line, "unterminated quoted value");
    }

    private static string FirstWord(string input, out string rest)
    {
        input = input.Trim();
        int end = input.IndexOfAny(' ', '\t');
        if (end < 0)
        {
            rest = string.Empty;
            return input;
        }
        rest = input[(end + 1)..].Trim();
        return input[..end];
    }

    private static long ParseFrames(string time, int line)
    {
        string[] parts = time.Split(':');
        if (parts.Length != 3
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out long minutes)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out long seconds)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long frames)
            || seconds >= 60 || frames >= 75)
            throw Invalid(line, "invalid INDEX timestamp (expected mm:ss:ff at 75 fps)");
        try
        {
            return checked((minutes * 60 + seconds) * 75 + frames);
        }
        catch (OverflowException)
        {
            throw Invalid(line, "INDEX timestamp is too large");
        }
    }

    private static FormatException Invalid(int line, string message) => new($"line {line}: {message}");
}
