using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FeatherW8.Audio;
using FeatherW8.Core;
using NAudio.Wave;

Texts.Initialize(AppLanguage.English);
string folder = Path.Combine(Path.GetTempPath(), "FeatherW8.CueTests." + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    TestCueQueue(folder);
    TestMalformedCue(folder);
    TestCueRanges();
    if (args.Length > 0)
        TestExistingCue(args[0]);
    Console.WriteLine("CUE tests passed.");
}
finally
{
    Directory.Delete(folder, recursive: true);
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new Exception(message);
}

static void WriteSilentWave(string path, int seconds)
{
    var format = new WaveFormat(44100, 16, 2);
    using var writer = new WaveFileWriter(path, format);
    byte[] silence = new byte[format.AverageBytesPerSecond];
    for (int i = 0; i < seconds; i++)
        writer.Write(silence, 0, silence.Length);
}

static void TestCueQueue(string folder)
{
    string first = Path.Combine(folder, "A image.wav");
    string second = Path.Combine(folder, "Part 2.wav");
    string other = Path.Combine(folder, "other.mp3");
    WriteSilentWave(first, 61);
    WriteSilentWave(second, 2);
    File.WriteAllBytes(other, []);
    string cue = Path.Combine(folder, "Z album.cue");
    File.WriteAllText(cue, """
        REM GENRE Metal
        TITLE "Album title"
        FILE "A image.wav" WAVE
          TRACK 01 AUDIO
            TITLE "Opening"
            INDEX 01 00:00:00
          TRACK 02 AUDIO
            TITLE "Next song"
            INDEX 00 00:59:00
            INDEX 01 01:00:00
        FILE "Part 2.wav" WAVE
          TRACK 03 AUDIO
            TITLE "Last song"
            INDEX 01 00:00:00
        """, Encoding.UTF8);

    using var diagnostics = new StringWriter();
    List<QueueTrack> fromCue = TrackQueueBuilder.Build([cue], diagnostics);
    Check(fromCue.Count == 3 && diagnostics.ToString().Length == 0, "explicit CUE must expand to three tracks");
    Check(fromCue[0] == new QueueTrack(first, "Opening", 1, new CueRange(0, 4500)), "track 1 must end at track 2 INDEX 01");
    Check(fromCue[1] == new QueueTrack(first, "Next song", 2, new CueRange(4500, null)), "track 2 must end at file boundary");
    Check(fromCue[2] == new QueueTrack(second, "Last song", 3, new CueRange(0, null)), "second FILE must reset positions");

    List<QueueTrack> fromFolder = TrackQueueBuilder.Build([folder], diagnostics);
    Check(fromFolder.Count == 4 && fromFolder.Count(t => t.Path == first) == 2 &&
          fromFolder.Count(t => t.Path == second) == 1 && fromFolder.Count(t => t.Path == other) == 1,
        "folder must not enqueue CUE image files again");
    Check(TrackQueueBuilder.Build([cue, first], diagnostics).Count == 4, "explicit raw file must remain playable");
}

static void TestMalformedCue(string folder)
{
    string badAudio = Path.Combine(folder, "Broken.flac");
    File.WriteAllBytes(badAudio, []);
    File.WriteAllText(Path.Combine(folder, "Broken.cue"), "FILE \"Broken.flac\" WAVE\n  TRACK 01 AUDIO\n");
    using var diagnostics = new StringWriter();
    List<QueueTrack> tracks = TrackQueueBuilder.Build([folder], diagnostics);
    Check(tracks.Count(t => t.Path == badAudio) == 1 && tracks.All(t => t.Path != badAudio || t.Range is null),
        "malformed CUE must leave its raw FLAC available");
    Check(diagnostics.ToString().Contains("INDEX 01"), "malformed CUE must report its problem");
    string missing = Path.Combine(folder, "Missing.cue");
    File.WriteAllText(missing, "FILE \"missing.flac\" WAVE\n TRACK 01 AUDIO\n INDEX 01 00:00:00\n");
    Check(TrackQueueBuilder.Build([missing], diagnostics).Count == 0 && diagnostics.ToString().Contains("not found"),
        "missing referenced file must not create a broken queue entry");

    string overrunAudio = Path.Combine(folder, "One second.wav");
    WriteSilentWave(overrunAudio, 1);
    File.WriteAllText(Path.Combine(folder, "Overrun.cue"), "FILE \"One second.wav\" WAVE\n TRACK 01 AUDIO\n INDEX 01 00:10:00\n");
    List<QueueTrack> withOverrun = TrackQueueBuilder.Build([folder], diagnostics);
    Check(withOverrun.Count(t => t.Path == overrunAudio) == 1 &&
          withOverrun.All(t => t.Path != overrunAudio || t.Range is null) &&
          diagnostics.ToString().Contains("outside its audio file"),
        "out-of-bounds CUE must not hide a playable raw audio file");

    string dsd = Path.Combine(folder, "Unsupported.dsf");
    File.WriteAllBytes(dsd, []);
    string dsdCue = Path.Combine(folder, "Unsupported.cue");
    File.WriteAllText(dsdCue, "FILE \"Unsupported.dsf\" WAVE\n TRACK 01 AUDIO\n INDEX 01 00:00:00\n");
    Check(TrackQueueBuilder.Build([dsdCue], diagnostics).Count == 0 &&
          TrackQueueBuilder.Build([folder], diagnostics).Any(t => t.Path == dsd && t.Range is null),
        "CUE must never wrap DSD as PCM, and folder scan must keep the raw DSD file");
}

static void TestExistingCue(string cuePath)
{
    using var diagnostics = new StringWriter();
    List<QueueTrack> tracks = TrackQueueBuilder.Build([cuePath], diagnostics);
    Check(tracks.Count > 0 && diagnostics.ToString().Length == 0, "real CUE must parse without diagnostics");
    foreach (QueueTrack cueTrack in tracks)
    {
        WaveStream decoder = AudioSourceFactory.Open(cueTrack.Path);
        using var track = new CueRangeWaveStream(decoder, cueTrack.Range!);
        Check(track.Length > 0 && track.Position == 0, "real CUE track must have a valid range");
        byte[] buffer = new byte[track.WaveFormat.AverageBytesPerSecond / 10];
        Check(track.Read(buffer, 0, buffer.Length) > 0, "real CUE track must decode audio at its start");
    }
    Console.WriteLine($"Verified {tracks.Count} tracks from {cuePath}");
}

static void TestCueRanges()
{
    foreach (int rate in new[] { 44100, 192000 })
    {
        using var raw = new FakeWaveStream(rate, 3);
        using (var track = new CueRangeWaveStream(raw, new CueRange(75, 150)))
        {
            int second = rate * raw.WaveFormat.BlockAlign;
            Check(track.Length == second && track.Position == 0 && raw.Position == second,
                "CUE track must start at INDEX 01 and last exactly one second");
            byte[] data = new byte[second + 128];
            Check(track.Read(data, 0, data.Length) == second && track.Position == second && track.Read(data, 0, 128) == 0,
                "read must end at next track INDEX 01 without crossing it");
            track.Position = 0;
            Check(track.Read(data, 0, 128) == 128 && raw.Position == second + 128, "seek must be relative to CUE start");
            Check(track.Seek(-128, SeekOrigin.End) == second - 128, "seek from end must use track length");
            track.Position = long.MaxValue;
            Check(track.Position == second, "seeking past track end must clamp");
        }
        Check(raw.Disposed, "disposing a track must release its decoder");
    }
}

internal sealed class FakeWaveStream(int rate, int durationSeconds) : WaveStream
{
    private long _position;
    public bool Disposed { get; private set; }
    public override WaveFormat WaveFormat { get; } = new(rate, 16, 2);
    public override long Length => (long)WaveFormat.AverageBytesPerSecond * durationSeconds;
    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, Length);
    }
    public override int Read(byte[] buffer, int offset, int count)
    {
        int length = (int)Math.Min(count, Length - Position);
        buffer.AsSpan(offset, length).Fill(0x55);
        _position += length;
        return length;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) Disposed = true;
        base.Dispose(disposing);
    }
}
