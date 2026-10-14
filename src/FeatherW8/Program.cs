using System.Text;
using FeatherW8.Core;
using FeatherW8.UI;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        AppSettings.Initialize();
        Texts.Initialize(AppSettings.Language);
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        if (args.Length == 1 && args[0] is "--help" or "-h" or "/?")
        {
            PrintUsage();
            return 0;
        }

        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine(Texts.Get("WindowsOnly"));
            return 1;
        }

        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            Console.Error.WriteLine(Texts.Get("TerminalRequired"));
            return 2;
        }

        var tracks = TrackQueueBuilder.Build(args, Console.Error);
        if (tracks.Count == 0)
        {
            Console.Error.WriteLine(Texts.Get("NoSupportedFiles"));
            return 2;
        }

        using var player = new PlayerController(tracks);
        bool oldCursorVisible = true;

        try
        {
            Console.Title = "FeatherW8";
            oldCursorVisible = Console.CursorVisible;
            Console.CursorVisible = false;
            Console.Clear();

            player.Start();
            while (!player.ExitRequested)
            {
                while (Console.KeyAvailable)
                {
                    player.HandleKey(Console.ReadKey(intercept: true));
                }

                player.Update();
                TerminalUi.Draw(player);
                await Task.Delay(80).ConfigureAwait(false);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.ResetColor();
            Console.WriteLine();
            Console.Error.WriteLine(Texts.Format("PlayerError", ex.Message));
            return 1;
        }
        finally
        {
            try
            {
                Console.CursorVisible = oldCursorVisible;
                Console.ResetColor();
                Console.WriteLine();
            }
            catch
            {
                // The terminal may have closed while playback was active.
            }
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine(Texts.Get("UsageTitle"));
        Console.WriteLine();
        Console.WriteLine(Texts.Get("UsageLabel"));
        Console.WriteLine(Texts.Get("UsageFile"));
        Console.WriteLine(Texts.Get("UsageFolder"));
        Console.WriteLine();
        Console.WriteLine(Texts.Get("UsageFolderNote"));
        Console.WriteLine(Texts.Get("UsageFormats"));
        Console.WriteLine(Texts.Get("UsageHelp"));
    }
}
