using System.ComponentModel;
using System.Diagnostics;
using HidSharp;
using Nefarius.ViGEm.Client;
using Nefarius.ViGEm.Client.Targets;
using Nefarius.ViGEm.Client.Targets.Xbox360;

// Lee el Pro Controller y lo expone como un unico Xbox 360 virtual, que vive mientras la app este abierta
// aunque el fisico se desconecte o se reconecte.
sealed class Bridge
{
    readonly Settings cfg;
    readonly CancellationToken ct;
    readonly ViGEmClient client = new();
    readonly IXbox360Controller pad;
    volatile byte rumble;

    public volatile string Text = "Esperando el mando...";
    public volatile bool Connected;
    public volatile bool LowBattery;

    public Bridge(Settings cfg, CancellationToken ct)
    {
        this.cfg = cfg; this.ct = ct;
        pad = client.CreateXbox360Controller();
        pad.AutoSubmitReport = false;
        pad.FeedbackReceived += (_, e) => rumble = Math.Max(e.LargeMotor, e.SmallMotor);
        // ViGEmBus 1.17 falla de forma esporadica la primera vez (Win32Exception sin codigo): reintentar
        for (int i = 1; ; i++)
        {
            try { pad.Connect(); break; }
            catch (Win32Exception) when (i < 5) { Thread.Sleep(500); }
        }
        // ViGEmBus 1.17 expone un estado basura hasta que llega un reporte distinto de cero: forzar uno y volver a neutro
        pad.SetAxisValue(Xbox360Axis.LeftThumbX, 1); pad.SubmitReport();
        Neutral();
    }

    void Neutral() { pad.ResetReport(); pad.SubmitReport(); }

    public void Run()
    {
        try
        {
            Hide.Cleanup();
            Hide.ReleaseOnExit();
            if (!Hide.Available) Log.Write("HidHide no esta instalado: los juegos veran tambien el mando fisico");
            while (!ct.IsCancellationRequested)
            {
                // USB primero: la entrada Bluetooth queda "fantasma" mientras el cable esta puesto y escribirle bloquea
                var devs = DeviceList.Local.GetHidDevices(Pro.Vid, Pro.Pid)
                    .OrderBy(d => d.DevicePath.Contains("00001124", StringComparison.OrdinalIgnoreCase));
                foreach (var dev in devs)
                {
                    if (ct.IsCancellationRequested) break;
                    try { Open(dev); }
                    catch (Exception e) { Log.Write($"Sesion terminada: {e.GetType().Name} {e.Message}"); }
                    Connected = false; LowBattery = false; Text = "Esperando el mando...";
                    Neutral();
                }
                ct.WaitHandle.WaitOne(1000);
            }
        }
        finally
        {
            Hide.Release();
            pad.Disconnect();
            client.Dispose();
        }
    }

    void Open(HidDevice dev)
    {
        if (!dev.TryOpen(out var s)) return;
        using var _s = s;
        Hide.Apply(dev.DevicePath); // se suelta al salir de la app (Run), no al desconectar el mando
        Session(dev, s);
    }

    void Session(HidDevice dev, HidStream s)
    {
        s.ReadTimeout = 1500;
        int outLen = dev.GetMaxOutputReportLength();
        var buf = new byte[dev.GetMaxInputReportLength()];
        bool usb = !dev.DevicePath.Contains("00001124", StringComparison.OrdinalIgnoreCase);
        Pro.Init(s, outLen, usb);

        Frame? Next() { int n = s.Read(buf); return Pro.Parse(buf, n); }

        Text = "Calibrando, no toques los sticks...";
        double lx = 0, ly = 0, rx = 0, ry = 0; int count = 0;
        while (count < 40 && !ct.IsCancellationRequested)
            if (Next() is { } f) { lx += f.LX; ly += f.LY; rx += f.RX; ry += f.RY; count++; }
        if (count == 0) return;
        var L = new Stick { Cx = lx / count, Cy = ly / count };
        var R = new Stick { Cx = rx / count, Cy = ry / count };

        string via = usb ? "USB" : "Bluetooth";
        Connected = true;
        Log.Write($"Conectado por {via}. Centros L=({L.Cx:F0},{L.Cy:F0}) R=({R.Cx:F0},{R.Cy:F0})");

        bool buzzing = false;
        int lastBat = -1;
        while (!ct.IsCancellationRequested)
        {
            if (Next() is not { } fr) continue;
            if (fr.Bat != lastBat)
            {
                lastBat = fr.Bat;
                var (pct, charging) = Pro.Battery(fr.Bat);
                LowBattery = pct <= 25 && !charging;
                Log.Write($"Bateria {pct} %{(charging ? " cargando" : "")} (byte 0x{fr.Bat:X2})");
                Text = $"Conectado por {via} - bateria {pct} %" + (charging ? " (cargando)" : "")
                    + (Hide.Available ? "" : " (sin HidHide)");
            }
            Map(fr, L, R);
            pad.SubmitReport();

            // shortcut: el clon solo vibra a una intensidad (probado: amplitud y pulsos no cambian nada); on/off
            bool want = rumble > 0;
            if (want || buzzing)
            {
                Pro.Rumble(s, outLen, want ? Pro.Buzz : Pro.Idle);
                buzzing = want;
            }
        }
    }

    bool capturePrev;

    void ScreenshotOnCapture(bool pressed)
    {
        if (pressed && !capturePrev && cfg.Capture) Task.Run(Screenshot.Take); // fuera del hilo del mando: guardar tarda
        capturePrev = pressed;
    }

    void Map(Frame f, Stick L, Stick R)
    {
        static bool Has(byte v, int m) => (v & m) != 0;
        bool labels = cfg.Labels;
        ScreenshotOnCapture(Has(f.B4, 0x20));
        pad.SetButtonState(Xbox360Button.A, Has(f.B3, labels ? 0x08 : 0x04));
        pad.SetButtonState(Xbox360Button.B, Has(f.B3, labels ? 0x04 : 0x08));
        pad.SetButtonState(Xbox360Button.X, Has(f.B3, labels ? 0x02 : 0x01));
        pad.SetButtonState(Xbox360Button.Y, Has(f.B3, labels ? 0x01 : 0x02));
        pad.SetButtonState(Xbox360Button.RightShoulder, Has(f.B3, 0x40));
        pad.SetButtonState(Xbox360Button.Back, Has(f.B4, 0x01));
        pad.SetButtonState(Xbox360Button.Start, Has(f.B4, 0x02));
        pad.SetButtonState(Xbox360Button.RightThumb, Has(f.B4, 0x04));
        pad.SetButtonState(Xbox360Button.LeftThumb, Has(f.B4, 0x08));
        pad.SetButtonState(Xbox360Button.Guide, Has(f.B4, 0x10));
        pad.SetButtonState(Xbox360Button.Down, Has(f.B5, 0x01));
        pad.SetButtonState(Xbox360Button.Up, Has(f.B5, 0x02));
        pad.SetButtonState(Xbox360Button.Right, Has(f.B5, 0x04));
        pad.SetButtonState(Xbox360Button.Left, Has(f.B5, 0x08));
        pad.SetButtonState(Xbox360Button.LeftShoulder, Has(f.B5, 0x40));
        pad.SetSliderValue(Xbox360Slider.RightTrigger, (byte)(Has(f.B3, 0x80) ? 255 : 0));
        pad.SetSliderValue(Xbox360Slider.LeftTrigger, (byte)(Has(f.B5, 0x80) ? 255 : 0));
        var (lx, ly) = L.Norm(f.LX, f.LY);
        var (rx, ry) = R.Norm(f.RX, f.RY);
        pad.SetAxisValue(Xbox360Axis.LeftThumbX, (short)(lx * 32767));
        pad.SetAxisValue(Xbox360Axis.LeftThumbY, (short)(ly * 32767));
        pad.SetAxisValue(Xbox360Axis.RightThumbX, (short)(rx * 32767));
        pad.SetAxisValue(Xbox360Axis.RightThumbY, (short)(ry * 32767));
    }
}
