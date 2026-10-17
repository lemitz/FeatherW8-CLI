using System.Reflection;
using System.Runtime.InteropServices;
using FeatherW8.Audio;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace FeatherW8.Core;

/// <summary>
/// Plays raw DSD through an ASIO driver in its native DSD mode (ASIO 2.2 "kAsioSetIoFormat").
/// The source is a <see cref="DsdWaveStream"/> (channel-interleaved bytes, MSB first); the bits go to
/// the driver untouched. Everything not covered by audio data - pause, end of track, errors - is
/// written as DSD silence (0x69), never as zeros.
/// <para>
/// NAudio's own ASIO classes are PCM-only, so this drives the public <see cref="AsioDriver"/> wrapper
/// directly. Like all ASIO drivers it must be created, started and disposed on an STA thread.
/// </para>
/// </summary>
internal sealed class NativeDsdAsioPlayer : IWavePlayer
{
    private const int kAsioCanDoIoFormat = 0x23112004;
    private const int kAsioSetIoFormat = 0x23111961;
    private const int IoFormatStructSize = 4 + 512;
    private const int BufferInfoSize = 24; // ASIOBufferInfo on x64: BOOL, long, void*, void*
    private const int TailBuffers = 3;     // silent buffers played after the last audio so the DAC drains

    private readonly AsioDriver _driver;
    private readonly string _driverName;
    private readonly object _stateLock = new();

    private IWaveProvider? _source;
    private AsioCallbacks _callbacks;
    private IntPtr _bufferInfos;
    private IntPtr[][] _buffers = [];
    private byte[] _readBuffer = [];
    private byte[] _channelScratch = [];
    private byte[] _silence = [];
    private int _channels;
    private int _bytesPerChannelPerBuffer;
    private bool _reverseBits;
    private bool _buffersCreated;
    private bool _started;
    private bool _dsdModeSet;
    private bool _disposed;

    private volatile PlaybackState _state = PlaybackState.Stopped;
    private volatile bool _sourceDone;
    private int _tailRemaining = TailBuffers;
    private int _stoppedRaised;

    public NativeDsdAsioPlayer(string driverName)
    {
        _driverName = driverName;
        _driver = AsioDriver.GetAsioDriverByName(driverName);
        try
        {
            if (!_driver.Init(IntPtr.Zero))
                throw new InvalidOperationException(_driver.GetErrorMessage());
        }
        catch
        {
            try { _driver.ReleaseComAsioDriver(); } catch { }
            throw;
        }
    }

    public float Volume
    {
        get => 1f;
        set { /* DSD cannot be attenuated without converting to PCM; use the DAC's volume control. */ }
    }

    public PlaybackState PlaybackState => _state;
    public WaveFormat OutputWaveFormat => _source?.WaveFormat ?? new WaveFormat(8, 8, 1);
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    public string DriverName => _driverName;

    public void Init(IWaveProvider waveProvider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _source = waveProvider;

        // The stand-in format of DsdWaveStream is 8-bit at DSD rate / 8.
        _channels = waveProvider.WaveFormat.Channels;
        double dsdRate = (double)waveProvider.WaveFormat.SampleRate * 8;

        _driver.GetChannels(out _, out int outputs);
        if (outputs < _channels)
            throw new NotSupportedException(Texts.Get("AudioModeChannelsUnsupported"));

        if (!TrySetIoFormat(dsd: true, probeOnly: true))
            throw new NotSupportedException(Texts.Get("AudioModeDsdDriverUnsupported"));
        if (!TrySetIoFormat(dsd: true, probeOnly: false))
            throw new NotSupportedException(Texts.Get("AudioModeDsdDriverUnsupported"));
        _dsdModeSet = true;

        if (!_driver.CanSampleRate(dsdRate))
            throw new NotSupportedException(Texts.Get("AudioModeFormatUnsupported"));
        _driver.SetSampleRate(dsdRate);

        for (int c = 0; c < _channels; c++)
        {
            AsioSampleType type = _driver.GetChannelInfo(c, false).type;
            if (type == AsioSampleType.DSDInt8MSB1)
                _reverseBits = false;
            else if (type == AsioSampleType.DSDInt8LSB1)
                _reverseBits = true;
            else
                throw new NotSupportedException(Texts.Get("AudioModeDsdDriverUnsupported"));
        }

        // Buffer sizes are counted in DSD samples (bits); eight of them make a byte.
        _driver.GetBufferSize(out int min, out int max, out int preferred, out int granularity);
        int bufferBits = ChooseBufferBits(min, max, preferred, granularity);
        _bytesPerChannelPerBuffer = bufferBits / 8;
        _readBuffer = new byte[_bytesPerChannelPerBuffer * _channels];
        _channelScratch = new byte[_bytesPerChannelPerBuffer];
        _silence = new byte[_bytesPerChannelPerBuffer];
        Array.Fill(_silence, DsdWaveStream.SilenceByte);

        _bufferInfos = Marshal.AllocHGlobal(BufferInfoSize * _channels);
        for (int c = 0; c < _channels; c++)
        {
            IntPtr info = _bufferInfos + c * BufferInfoSize;
            Marshal.WriteInt32(info, 0, 0);          // isInput = false
            Marshal.WriteInt32(info, 4, c);          // channelNum
            Marshal.WriteIntPtr(info, 8, IntPtr.Zero);
            Marshal.WriteIntPtr(info, 16, IntPtr.Zero);
        }

        _callbacks = BuildCallbacks();
        _driver.CreateBuffers(_bufferInfos, _channels, bufferBits, ref _callbacks);
        _buffersCreated = true;

        _buffers = new IntPtr[_channels][];
        for (int c = 0; c < _channels; c++)
        {
            IntPtr info = _bufferInfos + c * BufferInfoSize;
            _buffers[c] = [Marshal.ReadIntPtr(info, 8), Marshal.ReadIntPtr(info, 16)];
        }

        // Before the driver starts, both halves hold DSD silence - never leftover zeros.
        for (int c = 0; c < _channels; c++)
        {
            FillSilence(_buffers[c][0]);
            FillSilence(_buffers[c][1]);
        }
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_buffersCreated)
            throw new InvalidOperationException("Init must be called first.");

        lock (_stateLock)
        {
            if (!_started)
            {
                _sourceDone = false;
                Volatile.Write(ref _tailRemaining, TailBuffers);
                Interlocked.Exchange(ref _stoppedRaised, 0);
                _state = PlaybackState.Playing;
                _driver.Start();
                _started = true;
                return;
            }

            _state = PlaybackState.Playing;
        }
    }

    public void Pause()
    {
        if (_state == PlaybackState.Playing)
            _state = PlaybackState.Paused; // the driver keeps running and receives silence
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            _state = PlaybackState.Stopped;
            if (_started)
            {
                _started = false;
                try { _driver.Stop(); } catch { }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _state = PlaybackState.Stopped;
        try
        {
            if (_started)
            {
                _started = false;
                try { _driver.Stop(); } catch { }
            }

            if (_buffersCreated)
            {
                _buffersCreated = false;
                try { _driver.DisposeBuffers(); } catch { }
            }

            if (_dsdModeSet)
            {
                _dsdModeSet = false;
                try { TrySetIoFormat(dsd: false, probeOnly: false); } catch { } // leave the driver in PCM mode
            }
        }
        finally
        {
            try { _driver.ReleaseComAsioDriver(); } catch { }
            if (_bufferInfos != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(_bufferInfos);
                _bufferInfos = IntPtr.Zero;
            }
        }
    }

    // ------------------------------------------------------------------ audio callback

    private void OnBufferSwitch(int index, bool directProcess)
    {
        try
        {
            if (_disposed || !_buffersCreated || index is < 0 or > 1)
                return;

            bool producing = _state == PlaybackState.Playing && !_sourceDone;
            int filled = 0;

            if (producing)
            {
                filled = ReadSource();
                if (filled < _readBuffer.Length)
                    _sourceDone = true;
            }

            if (filled > 0)
            {
                int ch = _channels;
                for (int c = 0; c < ch; c++)
                    Deinterleave(c, filled / ch, _buffers[c][index]);
            }
            else
            {
                for (int c = 0; c < _channels; c++)
                    FillSilence(_buffers[c][index]);
            }

            try { _driver.OutputReady(); } catch { }

            if (_sourceDone && filled == 0 && _state == PlaybackState.Playing)
            {
                if (Interlocked.Decrement(ref _tailRemaining) <= 0)
                    RaiseStopped(null);
            }
        }
        catch (Exception ex)
        {
            // An exception must never cross into the driver's native code.
            try
            {
                for (int c = 0; c < _channels; c++)
                    FillSilence(_buffers[c][index & 1]);
            }
            catch { }

            RaiseStopped(ex);
        }
    }

    private int ReadSource()
    {
        int total = 0;
        IWaveProvider source = _source!;
        while (total < _readBuffer.Length)
        {
            int n = source.Read(_readBuffer.AsSpan(total));
            if (n <= 0)
                break;
            total += n;
        }

        // Channel-aligned only; any remainder is dropped (DsdWaveStream already aligns).
        return total - total % _channels;
    }

    private void Deinterleave(int channel, int framesAvailable, IntPtr destination)
    {
        int ch = _channels;
        int n = _bytesPerChannelPerBuffer;
        byte[] src = _readBuffer;
        byte[] dst = _channelScratch;
        bool reverse = _reverseBits;

        int i = 0;
        for (; i < framesAvailable; i++)
        {
            byte b = src[i * ch + channel];
            dst[i] = reverse ? ReverseBits(b) : b;
        }

        for (; i < n; i++)
            dst[i] = DsdWaveStream.SilenceByte;

        Marshal.Copy(dst, 0, destination, n);
    }

    private void FillSilence(IntPtr destination) =>
        Marshal.Copy(_silence, 0, destination, _silence.Length);

    private static byte ReverseBits(byte b)
    {
        int v = b, r = 0;
        for (int i = 0; i < 8; i++)
        {
            r = (r << 1) | (v & 1);
            v >>= 1;
        }

        return (byte)r;
    }

    private void OnSampleRateChanged(double rate)
    {
        // The rate is fixed for the whole track; a driver-initiated change is treated as a reset request.
    }

    private int OnAsioMessage(AsioMessageSelector selector, int value, IntPtr message, IntPtr opt)
    {
        switch (selector)
        {
            case AsioMessageSelector.kAsioSelectorSupported:
                return value is (int)AsioMessageSelector.kAsioEngineVersion or (int)AsioMessageSelector.kAsioResetRequest ? 1 : 0;
            case AsioMessageSelector.kAsioEngineVersion:
                return 2;
            case AsioMessageSelector.kAsioResetRequest:
                RaiseStopped(new InvalidOperationException("The ASIO driver requested a reset."));
                return 1;
            case AsioMessageSelector.kAsioSupportsTimeInfo:
                return 0; // plain bufferSwitch is used
            default:
                return 0;
        }
    }

    private IntPtr OnBufferSwitchTimeInfo(IntPtr time, int index, bool directProcess)
    {
        OnBufferSwitch(index, directProcess);
        return IntPtr.Zero;
    }

    private void RaiseStopped(Exception? error)
    {
        if (Interlocked.Exchange(ref _stoppedRaised, 1) != 0)
            return;

        _state = PlaybackState.Stopped;
        // Never call back into the driver from inside its own callback: tell the app on another thread.
        ThreadPool.QueueUserWorkItem(_ => PlaybackStopped?.Invoke(this, new StoppedEventArgs(error)));
    }

    // ------------------------------------------------------------------ driver helpers

    private bool TrySetIoFormat(bool dsd, bool probeOnly)
    {
        IntPtr block = Marshal.AllocHGlobal(IoFormatStructSize);
        try
        {
            for (int i = 0; i < IoFormatStructSize; i++)
                Marshal.WriteByte(block, i, 0);
            Marshal.WriteInt32(block, 0, dsd ? 1 : 0); // kASIOPCMFormat = 0, kASIODSDFormat = 1

            try
            {
                _driver.Future(probeOnly ? kAsioCanDoIoFormat : kAsioSetIoFormat, block);
                return true;
            }
            catch
            {
                return false;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(block);
        }
    }

    private static int ChooseBufferBits(int min, int max, int preferred, int granularity)
    {
        // A somewhat larger buffer than the driver's default costs nothing for playback and makes
        // file reads in the callback safer at high DSD rates.
        int candidate = Math.Clamp(Math.Max(preferred, 65536), min, max);
        bool valid = granularity switch
        {
            0 => candidate == preferred,
            < 0 => (candidate & (candidate - 1)) == 0,
            _ => (candidate - min) % granularity == 0
        };

        int bits = valid ? candidate : preferred;
        return Math.Max(8, bits - bits % 8);
    }

    private AsioCallbacks BuildCallbacks()
    {
        // The delegate types of AsioCallbacks are internal to NAudio, so they are created by reflection.
        object boxed = new AsioCallbacks();
        void Bind(string field, string method)
        {
            FieldInfo f = typeof(AsioCallbacks).GetField(field)
                ?? throw new MissingFieldException(typeof(AsioCallbacks).FullName, field);
            MethodInfo m = typeof(NativeDsdAsioPlayer).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingMethodException(typeof(NativeDsdAsioPlayer).FullName, method);
            f.SetValue(boxed, Delegate.CreateDelegate(f.FieldType, this, m));
        }

        Bind(nameof(AsioCallbacks.pbufferSwitch), nameof(OnBufferSwitch));
        Bind(nameof(AsioCallbacks.psampleRateDidChange), nameof(OnSampleRateChanged));
        Bind(nameof(AsioCallbacks.pasioMessage), nameof(OnAsioMessage));
        Bind(nameof(AsioCallbacks.pbufferSwitchTimeInfo), nameof(OnBufferSwitchTimeInfo));
        return (AsioCallbacks)boxed;
    }
}
