using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FeatherW8.Core;

internal sealed class AudioOutputUnavailableException : Exception
{
    public AudioOutputUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

internal sealed class AudioOutputSelection : IDisposable
{
    private readonly IDisposable? _endpoint;
    private int _disposed;

    public AudioOutputSelection(IWavePlayer player, string label, bool isStrict, IDisposable? endpoint = null)
    {
        Player = player;
        Label = label;
        IsStrict = isStrict;
        _endpoint = endpoint;
    }

    public IWavePlayer Player { get; }
    public string Label { get; }
    public bool IsStrict { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            Player.Dispose();
        }
        finally
        {
            _endpoint?.Dispose();
        }
    }
}

internal static class AudioOutputFactory
{
    public static AudioOutputSelection Create(
        IWaveProvider source,
        AudioOutputPreference preference,
        bool forceShared = false)
    {
        if (forceShared)
            return CreateShared(source, preferExternalUsbDevice: preference is AudioOutputPreference.Auto or AudioOutputPreference.Asio);
        if (preference == AudioOutputPreference.Shared)
            return CreateShared(source, preferExternalUsbDevice: false);

        var failures = new List<Exception>();

        if (preference is AudioOutputPreference.Auto or AudioOutputPreference.Asio)
        {
            string[] drivers = GetAsioDrivers(failures);
            IEnumerable<string> candidates = preference == AudioOutputPreference.Auto
                ? PreferFiiO(drivers).Where(IsFiiODriver)
                : PreferFiiO(drivers);

            foreach (string driverName in candidates)
            {
                try
                {
                    return CreateAsio(source, driverName);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            }

            if (preference == AudioOutputPreference.Asio)
            {
                string message = drivers.Length == 0
                    ? Texts.Get("AudioModeNoAsioDriver")
                    : Texts.Get("AudioModeDeviceUnavailable");
                throw new AudioOutputUnavailableException(message, failures.LastOrDefault());
            }
        }

        if (preference is AudioOutputPreference.Auto or AudioOutputPreference.WasapiExclusive)
        {
            try
            {
                return CreateWasapiExclusive(source, preferExternalUsbDevice: preference == AudioOutputPreference.Auto);
            }
            catch (Exception ex)
            {
                failures.Add(ex);
            }
        }

        string reason = failures.Count > 0 && failures.All(IsFormatFailure)
            ? Texts.Get("AudioModeFormatUnsupported")
            : Texts.Get("AudioModeDeviceUnavailable");
        throw new AudioOutputUnavailableException(reason, failures.LastOrDefault());
    }

    private static AudioOutputSelection CreateAsio(IWaveProvider source, string driverName)
    {
        // NAudio 3 retains AsioOut as an IWavePlayer-compatible facade over AsioDevice.
        // Using the facade keeps seek, pause, stop, and error handling on the same path as WASAPI.
#pragma warning disable CS0618
        var output = new AsioOut(driverName);
#pragma warning restore CS0618
        try
        {
            if (output.DriverOutputChannelCount < source.WaveFormat.Channels)
                throw new NotSupportedException(Texts.Get("AudioModeChannelsUnsupported"));
            if (!output.IsSampleRateSupported(source.WaveFormat.SampleRate))
                throw new NotSupportedException(Texts.Get("AudioModeFormatUnsupported"));

            output.Init(source);
            string label = Texts.Format("AudioModeAsioLabel", driverName);
            return new AudioOutputSelection(output, label, isStrict: true);
        }
        catch
        {
            try { output.Dispose(); } catch { }
            throw;
        }
    }

    private static AudioOutputSelection CreateWasapiExclusive(IWaveProvider source, bool preferExternalUsbDevice)
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice? endpoint = null;
        WasapiPlayer? output = null;

        try
        {
            endpoint = SelectRenderEndpoint(enumerator, preferExternalUsbDevice, out string endpointName);
            output = new WasapiPlayerBuilder()
                .WithDevice(endpoint)
                .WithExclusiveMode()
                .WithRawMode()
                .WithEventSync()
                .WithLatency(120)
                .WithMmcssThreadPriority("Pro Audio")
                .Build();

            if (!output.IsFormatSupported(source.WaveFormat))
                throw new NotSupportedException(Texts.Get("AudioModeFormatUnsupported"));

            output.Init(source);
            string label = Texts.Format("AudioModeWasapiExclusiveLabel", endpointName);
            var selection = new AudioOutputSelection(output, label, isStrict: true, endpoint);
            output = null;
            endpoint = null;
            return selection;
        }
        finally
        {
            try
            {
                output?.Dispose();
            }
            finally
            {
                try
                {
                    endpoint?.Dispose();
                }
                finally
                {
                    enumerator.Dispose();
                }
            }
        }
    }

    private static AudioOutputSelection CreateShared(IWaveProvider source, bool preferExternalUsbDevice)
    {
        var enumerator = new MMDeviceEnumerator();
        MMDevice? endpoint = null;
        WasapiPlayer? output = null;

        try
        {
            endpoint = SelectRenderEndpoint(enumerator, preferExternalUsbDevice, out string endpointName);
            output = new WasapiPlayerBuilder()
                .WithDevice(endpoint)
                .WithSharedMode()
                .WithEventSync()
                .WithLatency(120)
                .Build();
            output.Init(source);

            string label = Texts.Format("AudioModeSharedLabel", endpointName);
            var selection = new AudioOutputSelection(output, label, isStrict: false, endpoint);
            output = null;
            endpoint = null;
            return selection;
        }
        finally
        {
            try
            {
                output?.Dispose();
            }
            finally
            {
                try
                {
                    endpoint?.Dispose();
                }
                finally
                {
                    enumerator.Dispose();
                }
            }
        }
    }

    private static MMDevice SelectRenderEndpoint(
        MMDeviceEnumerator enumerator,
        bool preferExternalUsbDevice,
        out string endpointName)
    {
        using MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        using MMDevice defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        string defaultId = defaultDevice.ID;

        var choices = new List<(string Id, string Name, bool IsFiiO, bool IsUsb)>();
        for (int i = 0; i < devices.Count; i++)
        {
            using MMDevice candidate = devices[i];
            string name = string.Join(" ", candidate.FriendlyName, candidate.DeviceFriendlyName, candidate.InstanceId);
            choices.Add((candidate.ID, candidate.FriendlyName, IsFiiOName(name), IsUsbName(name)));
        }

        var defaultChoice = choices.FirstOrDefault(choice =>
            choice.Id.Equals(defaultId, StringComparison.OrdinalIgnoreCase));

        (string Id, string Name, bool IsFiiO, bool IsUsb) selected = defaultChoice;
        if (preferExternalUsbDevice)
        {
            var fiio = choices.FirstOrDefault(choice => choice.IsFiiO);
            if (!string.IsNullOrEmpty(fiio.Id))
            {
                selected = fiio;
            }
            else
            {
                var usbChoices = choices.Where(choice => choice.IsUsb).ToList();
                if (usbChoices.Count == 1)
                    selected = usbChoices[0];
                else if (defaultChoice.IsUsb)
                    selected = defaultChoice;
            }
        }

        if (string.IsNullOrEmpty(selected.Id))
            throw new InvalidOperationException(Texts.Get("AudioModeDeviceUnavailable"));

        endpointName = string.IsNullOrWhiteSpace(selected.Name)
            ? Texts.Get("AudioModeDefaultDevice")
            : selected.Name;
        return enumerator.GetDevice(selected.Id);
    }

    private static string[] GetAsioDrivers(List<Exception> failures)
    {
        try
        {
#pragma warning disable CS0618
            return AsioOut.GetDriverNames() ?? Array.Empty<string>();
#pragma warning restore CS0618
        }
        catch (Exception ex)
        {
            failures.Add(ex);
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> PreferFiiO(IEnumerable<string> driverNames)
    {
        string[] names = driverNames.ToArray();
        return names.Where(IsFiiODriver).Concat(names.Where(name => !IsFiiODriver(name)));
    }

    private static bool IsFiiODriver(string name) =>
        name.Contains("FiiO", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("K11", StringComparison.OrdinalIgnoreCase);

    private static bool IsFiiOName(string name) => IsFiiODriver(name);

    private static bool IsUsbName(string name) =>
        name.Contains("USB", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("DAC", StringComparison.OrdinalIgnoreCase);

    private static bool IsFormatFailure(Exception exception) => exception is NotSupportedException;
}
