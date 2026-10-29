// Legend.ClientStub — placeholder "Unity client" for launcher smoke tests.
// Prints selected realm connection info and exits 0.

string host = "127.0.0.1";
int port = 7100;
string realmId = "";
string? extra = null;

for (int i = 0; i < args.Length; i++)
{
    string a = args[i];
    if (a == "--host" && i + 1 < args.Length) host = args[++i];
    else if (a == "--port" && i + 1 < args.Length) port = int.Parse(args[++i]);
    else if (a == "--realm-id" && i + 1 < args.Length) realmId = args[++i];
    else if (a is "--help" or "-h")
    {
        Console.WriteLine("Legend.ClientStub --host <h> --port <p> --realm-id <id>");
        return;
    }
    else extra = (extra is null ? a : extra + " " + a);
}

Console.WriteLine("[ClientStub] Legend client stub starting");
Console.WriteLine($"[ClientStub] host={host}");
Console.WriteLine($"[ClientStub] port={port}");
Console.WriteLine($"[ClientStub] realm-id={realmId}");
if (!string.IsNullOrWhiteSpace(extra))
    Console.WriteLine($"[ClientStub] extra={extra}");
Console.WriteLine("[ClientStub] OK — would hand off to Unity player here");
Environment.ExitCode = 0;
