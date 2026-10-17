using FeatherW8.Core;
using NAudio.Vorbis;
using NAudio.Wave;

namespace FeatherW8.Audio;

internal static class AudioSourceFactory
{
    public static WaveStream Open(string path)
    {
        string extension = Path.GetExtension(path);

        if (extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            // Windows Media Foundation decodes MP3 without shipping a separate codec binary.
            return new MediaFoundationReader(path);
        }

        if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            return new WaveFileReader(path);
        }

        if (extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".fla", StringComparison.OrdinalIgnoreCase))
        {
            return new FlacWaveStream(path);
        }

        if (extension.Equals(".opus", StringComparison.OrdinalIgnoreCase))
        {
            return new OggOpusWaveStream(path);
        }

        if (extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase))
        {
            return OggOpusWaveStream.IsOpusFile(path)
                ? new OggOpusWaveStream(path)
                : new VorbisWaveReader(path);
        }

        throw new NotSupportedException(Texts.Format("UnsupportedFormat", extension));
    }
}
