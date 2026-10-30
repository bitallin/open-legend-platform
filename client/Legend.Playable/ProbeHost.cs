using Legend.Playable.Net;

namespace Legend.Playable;

/// <summary>Connects and completes Handshake only, then exits. Used to check a live realm.</summary>
public static class ProbeHost
{
    public static int Run(LaunchOptions options)
    {
        try
        {
            using var client = new PlayableNetClient();
            client.ConnectAsync(options.Host, options.Port).GetAwaiter().GetResult();
            client.HandshakeAsync().GetAwaiter().GetResult();
            Console.WriteLine($"PROBE_OK {options.Host}:{options.Port} protocol={Legend.Contracts.ProtocolConstants.CurrentVersion}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PROBE_FAIL " + ex.Message);
            return 1;
        }
    }
}
