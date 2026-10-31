namespace Legend.Playable;

/// <summary>Launcher / CLI arguments. Defaults target the public trial realm.</summary>
public sealed class LaunchOptions
{
    public const string DefaultHost = "150.158.164.90";
    public const int DefaultPort = 7100;
    public const string DefaultRealmId = "realm-classic-01";

    public string Host { get; set; } = DefaultHost;
    public int Port { get; set; } = DefaultPort;
    public string RealmId { get; set; } = DefaultRealmId;
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Smoke { get; set; }
    public bool UiSmoke { get; set; }
    public bool Probe { get; set; }
    public bool ShowHelp { get; set; }
    /// <summary>When set, seed local InWorld chrome and dump key OL panel PNGs then exit.</summary>
    public string UiCaptureDir { get; set; } = "";

    public static string HelpText =>
        """
        LegendClient — 非 Unity 可玩客户端（协议 1.21.0）

        LegendClient.exe --host <主机> --port <端口> --realm-id <区服ID> [--user <账号> --pass <密码>]

        公网试玩：
          LegendClient.exe --host 150.158.164.90 --port 7100 --realm-id realm-classic-01

        操作：WASD/方向键移动，左键点地/选中/拾取，右键或 Space/J 攻击，Tab 切目标，
              B 背包，Enter 聊天，Z/G 拾取，Esc 关闭背包。

        --smoke      无窗口自检（编译与本地逻辑）
        --probe      只做握手（连上 --host/--port 后退出）
        --ui-smoke   打开窗口后立即退出（需显示器）
        --ui-capture <dir>  本地种子进世界并截关键 OL 窗到目录后退出（离线）
        --help
        """;

    public static LaunchOptions Parse(string[] args)
    {
        var opt = new LaunchOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--host":
                    opt.Host = Next(args, ref i, a);
                    break;
                case "--port":
                    if (!int.TryParse(Next(args, ref i, a), out int port) || port is < 1 or > 65535)
                        throw new FormatException("--port 必须是 1–65535");
                    opt.Port = port;
                    break;
                case "--realm-id":
                    opt.RealmId = Next(args, ref i, a);
                    break;
                case "--user":
                    opt.Username = Next(args, ref i, a);
                    break;
                case "--pass":
                    opt.Password = Next(args, ref i, a);
                    break;
                case "--smoke":
                    opt.Smoke = true;
                    break;
                case "--probe":
                    opt.Probe = true;
                    break;
                case "--ui-smoke":
                    opt.UiSmoke = true;
                    break;
                case "--ui-capture":
                    opt.UiCaptureDir = Next(args, ref i, a);
                    break;
                case "--help":
                case "-h":
                case "/?":
                    opt.ShowHelp = true;
                    break;
                default:
                    if (a.StartsWith('-'))
                        throw new FormatException($"未知参数 {a}");
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(opt.Host))
            throw new FormatException("--host 不能为空");
        if (string.IsNullOrWhiteSpace(opt.RealmId))
            throw new FormatException("--realm-id 不能为空");
        return opt;
    }

    private static string Next(string[] args, ref int i, string flag)
    {
        if (i + 1 >= args.Length)
            throw new FormatException($"{flag} 缺少取值");
        return args[++i];
    }
}
