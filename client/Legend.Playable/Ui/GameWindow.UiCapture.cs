using System.IO;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Legend.Playable.Ui;

public partial class GameWindow
{
    private async void RunUiCaptureAsync(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            _session.SeedUiCapture();
            ApplyChrome();
            await Delay(400);

            // OL-07-D party invite modal (independent) — no panel overlay
            OpenPanelLocal("");
            _session.Assist.Stop("capture");
            RefreshPhaseL();
            await Delay(120);
            await SaveShot(Path.Combine(dir, "OL-07-D.png"));

            _session.ClearPendingPartyForCapture();
            _session.Assist.Settings.Enabled = true;
            _session.Assist.TryStart(new Legend.ClientCore.AssistWorldView
            {
                MapId = "novice_plain",
                SelfX = 20,
                SelfY = 20,
                Hp = 800,
                MaxHp = 1000,
                Mp = 200,
                MaxMp = 300,
                NowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            }, out _);

            // OL-09-D guild near-full (skip RefreshGuildAsync — keep seed)
            _guildTab = "members";
            OpenPanelLocal("guild");
            RefreshPhaseL();
            await Delay(180);
            await SaveShot(Path.Combine(dir, "OL-09-D.png"));

            // OL-17-BF mail list+read+attach
            _mailTab = "inbox";
            _selectedMailId = "mail-capture-1";
            OpenPanelLocal("mail");
            RefreshPhaseL();
            await Delay(180);
            await SaveShot(Path.Combine(dir, "OL-17-BF.png"));

            // OL-08-J stall empty browse
            _stallTab = "browse";
            _stallQuery = "";
            _stallCat = "all";
            OpenPanelLocal("stall");
            RefreshPhaseL();
            await Delay(180);
            await SaveShot(Path.Combine(dir, "OL-08-J.png"));

            // OL-16-N assist floating chrome
            OpenPanelLocal("");
            RefreshPhaseL();
            await Delay(150);
            await SaveShot(Path.Combine(dir, "OL-16-N.png"));

            // OL-17-B mall packs
            _mallTab = "pack";
            OpenPanelLocal("mall");
            RefreshPhaseL();
            await Delay(180);
            await SaveShot(Path.Combine(dir, "OL-17-B.png"));

            // OL-16-K assist settings
            _assistTab = "fight";
            OpenPanelLocal("assist");
            RefreshPhaseL();
            await Delay(180);
            await SaveShot(Path.Combine(dir, "OL-16-K.png"));

            Console.WriteLine("UI_CAPTURE_DONE " + dir);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("UI_CAPTURE_FAIL " + ex);
        }
        finally
        {
            var close = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            close.Tick += (_, _) =>
            {
                close.Stop();
                Close();
            };
            close.Start();
        }
    }

    private void OpenPanelLocal(string id)
    {
        _openPanel = id ?? "";
        _panelFp = "";
        HidePlayerContext();
    }

    private static Task Delay(int ms)
    {
        var tcs = new TaskCompletionSource();
        var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            tcs.TrySetResult();
        };
        t.Start();
        return tcs.Task;
    }

    private async Task SaveShot(string path)
    {
        UpdateLayout();
        await Delay(50);
        var size = Bounds.Size;
        if (size.Width < 2 || size.Height < 2)
            size = new Size(Width > 0 ? Width : 1280, Height > 0 ? Height : 760);
        var pixelSize = new PixelSize(Math.Max(2, (int)Math.Ceiling(size.Width)), Math.Max(2, (int)Math.Ceiling(size.Height)));
        using var bmp = new RenderTargetBitmap(pixelSize, new Vector(96, 96));
        bmp.Render(this);
        bmp.Save(path);
        Console.WriteLine("UI_CAPTURE_SHOT " + path);
    }
}
