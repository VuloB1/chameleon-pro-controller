using System.Runtime.InteropServices;

static class App
{
    [DllImport("kernel32")] static extern bool AttachConsole(int pid);

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--selftest")) { AttachConsole(-1); SelfTest.Run(); return; }
        if (args.Contains("--cleanup")) { Hide.Cleanup(); return; } // lo usa el desinstalador: devuelve el mando si quedo oculto

        using var single = new Mutex(true, "Chameleon.Single", out bool first);
        if (!first) return; // dos instancias pelearian por el mando
        Settings.MigrateFromMandoSwitch();

        ApplicationConfiguration.Initialize();
        TrayContext ctx;
        try { ctx = new TrayContext(); }
        catch (Exception e) // ViGEmBus ausente o roto: sin el no hay mando virtual
        {
            MessageBox.Show($"{L.Get("novigem")}\n\n{e.Message}", "Chameleon",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.Run(ctx);
    }
}

sealed class TrayContext : ApplicationContext
{
    readonly NotifyIcon tray = new() { Visible = true };
    readonly Settings cfg = Settings.Load();
    readonly CancellationTokenSource cts = new();
    readonly Bridge bridge;
    readonly Thread worker;
    readonly Icon off = Load("tray-off.ico"), on = Load("tray-on.ico"), low = Load("tray-low.ico");
    readonly ToolStripMenuItem status = new() { Enabled = false };
    readonly ToolStripMenuItem labels = new() { CheckOnClick = true }, capture = new() { CheckOnClick = true },
        nameItem = new(), dead = new(), auto = new() { CheckOnClick = true }, langMenu = new(), quit = new();
    readonly ToolStripMenuItem updateItem = new();
    string updateVer = "", updateUrl = "";
    Icon? shown;

    public TrayContext()
    {
        L.Set(cfg.Lang);
        Log.Reset();
        Stick.Dead = cfg.Dead;
        bridge = new Bridge(cfg, cts.Token);
        worker = new Thread(bridge.Run) { IsBackground = true, Name = "bridge", Priority = ThreadPriority.AboveNormal }; // que un juego al 100 % de CPU no retrase el mando
        worker.Start();

        var menu = new ContextMenuStrip();
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());

        labels.Checked = cfg.Labels;
        labels.CheckedChanged += (_, _) => { cfg.Labels = labels.Checked; cfg.Save(); };
        menu.Items.Add(labels);

        capture.Checked = cfg.Capture;
        capture.CheckedChanged += (_, _) => { cfg.Capture = capture.Checked; cfg.Save(); };
        menu.Items.Add(capture);

        nameItem.Click += (_, _) =>
        {
            var n = Microsoft.VisualBasic.Interaction.InputBox(L.Get("nameprompt"), "Chameleon", cfg.Name).Trim();
            if (n.Length == 0) return; // cancelar o dejarlo vacio: se queda el actual
            cfg.Name = n.Length > 20 ? n[..20] : n; cfg.Save(); Refresh();
        };
        menu.Items.Add(nameItem);

        foreach (int pct in new[] { 0, 5, 8, 12, 18 })
        {
            var it = new ToolStripMenuItem($"{pct} %") { Checked = Math.Abs(cfg.Dead * 100 - pct) < 0.5 };
            it.Click += (_, _) =>
            {
                cfg.Dead = pct / 100.0; Stick.Dead = cfg.Dead; cfg.Save();
                foreach (ToolStripMenuItem o in dead.DropDownItems) o.Checked = o == it;
            };
            dead.DropDownItems.Add(it);
        }
        menu.Items.Add(dead);

        auto.Checked = Settings.Autostart;
        auto.CheckedChanged += (_, _) => Settings.Autostart = auto.Checked;
        menu.Items.Add(auto);

        var langItems = new List<(string code, ToolStripMenuItem item)>();
        var langs = new List<(string code, string label)> { ("auto", "Auto") };
        for (int n = 0; n < L.Names.Length; n++) langs.Add((L.Code(n), L.Names[n]));
        foreach (var (code, label) in langs)
        {
            var li = new ToolStripMenuItem(label) { Checked = cfg.Lang == code };
            li.Click += (_, _) =>
            {
                cfg.Lang = code; cfg.Save(); L.Set(code);
                foreach (var (c, i) in langItems) i.Checked = c == code;
                Localize(); Refresh();
            };
            langItems.Add((code, li)); langMenu.DropDownItems.Add(li);
        }
        menu.Items.Add(langMenu);

        updateItem.Visible = false;
        updateItem.Click += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(updateUrl) { UseShellExecute = true });
        menu.Items.Add(updateItem);

        menu.Items.Add(new ToolStripSeparator());
        quit.Click += (_, _) => Quit();
        menu.Items.Add(quit);
        tray.ContextMenuStrip = menu;
        Localize();

        // Busca version nueva al arrancar y despues cada 24 h (el mando suele estar horas en la bandeja)
        var upd = new System.Windows.Forms.Timer { Interval = 24 * 3600 * 1000 };
        upd.Tick += async (_, _) => await CheckUpdate(false);
        upd.Start();
        _ = CheckUpdate(true);

        // El hilo del puente solo publica estado; la interfaz lo lee aqui, sin cruzar hilos
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        Refresh();
    }

    async Task CheckUpdate(bool notify)
    {
        if (await Update.Check() is not { } u) return;
        updateVer = u.ver; updateUrl = u.url;
        updateItem.Visible = true; Localize();
        if (notify && cfg.NotifiedVersion != u.ver) // un solo aviso por version, el resto queda en el menu
        {
            cfg.NotifiedVersion = u.ver; cfg.Save();
            tray.ShowBalloonTip(5000, "Chameleon", string.Format(L.Get("update"), updateVer), ToolTipIcon.Info);
            tray.BalloonTipClicked += (_, _) => updateItem.PerformClick();
        }
    }

    void Localize()
    {
        updateItem.Text = string.Format(L.Get("update"), updateVer);
        labels.Text = L.Get("labels"); capture.Text = L.Get("capture"); nameItem.Text = L.Get("name");
        dead.Text = L.Get("dead"); auto.Text = L.Get("autostart"); quit.Text = L.Get("quit");
        langMenu.Text = L.Current == 1 ? L.Get("language") : L.Get("language") + " / Language"; // pista para quien cayo en un idioma que no lee
    }

    void Refresh()
    {
        string text = L.StatusText(bridge.St, Hide.Available);
        status.Text = text;
        var tip = $"{cfg.Name} - {text}";
        tray.Text = tip[..Math.Min(63, tip.Length)]; // el tooltip de Windows admite 63 caracteres
        var icon = !bridge.Connected ? off : bridge.LowBattery ? low : on; // rojo: bateria baja y sin cargar
        if (icon != shown) { tray.Icon = icon; shown = icon; }
    }

    void Quit()
    {
        cts.Cancel();
        worker.Join(4000); // el hilo libera el mando fisico (HidHide) y el virtual al terminar
        tray.Visible = false;
        ExitThread();
    }

    // .ico con varios tamanos incrustado en el exe: Windows elige el que encaja con el DPI de la bandeja
    static Icon Load(string name)
    {
        using var s = typeof(App).Assembly.GetManifestResourceStream(name)!;
        return new Icon(s, SystemInformation.SmallIconSize);
    }
}

static class SelfTest
{
    static void Check(bool ok, string what) { if (!ok) throw new Exception(what); }

    public static void Run()
    {
        try
        {
            // reporte real capturado de tu mando en reposo
            var r = new byte[] { 0x30, 0xB6, 0x80, 0x00, 0x80, 0x00, 0x01, 0x18, 0x80, 0x01, 0x18, 0x80 };
            var f = Pro.Parse(r, r.Length)!.Value;
            Check(f.LX == 0x801 && f.LY == 0x801 && f.RX == 0x801 && f.RY == 0x801, "parse sticks");
            Check(Pro.Parse(r, 5) is null, "reporte corto");
            Stick.Dead = 0.08;
            var s = new Stick { Cx = 2049, Cy = 2049 };
            Check(s.Norm(2049, 2049) == (0, 0), "reposo");
            Check(s.Norm(2049 + 100, 2049) == (0, 0), "zona muerta");
            var (x, y) = s.Norm(2049 + 1500, 2049);
            Check(Math.Abs(x - 1) < 1e-9 && y == 0, "tope");
            var (x2, y2) = s.Norm(2049 + 3000, 2049 - 3000);
            Check(Math.Sqrt(x2 * x2 + y2 * y2) <= 1 + 1e-9 && y2 < 0, "fuera de rango");
            Check(Pro.Battery(0x80) == (100, false) && Pro.Battery(0x90) == (100, true), "bateria llena");
            Check(Pro.Battery(0x40) == (50, false) && Pro.Battery(0x20) == (25, false) && Pro.Battery(0x00) == (5, false), "bateria media/baja");
            Check(Pro.Parse(r, r.Length)!.Value.Bat == 0x80, "parse bateria");
            Stick.Dead = 0;
            Check(new Stick { Cx = 2049, Cy = 2049 }.Norm(2049, 2049) == (0, 0), "zona muerta 0 sin NaN");
            foreach (var (k, v) in L.T) Check(v.Length == 6 && v.All(s => s.Length > 0), "idiomas: " + k);
            L.Set("xx"); Check(L.Current == 1, "idioma desconocido cae en ingles");
            L.Set("ja"); Check(L.StatusText(new(Kind.Connected, "USB", 50, true), true).Contains("USB") && L.StatusText(new(Kind.Connected, "USB", 50, true), true).Contains("50"), "texto de estado");
            L.Set("auto");
            var cur = new Version(1, 1, 0);
            Check(Update.Newer("v1.2.0", cur) && Update.Newer("1.1.1", cur) && !Update.Newer("v1.1.0", cur) && !Update.Newer("v1.0.9", cur) && !Update.Newer("basura", cur), "comparar versiones");
            // Sony: reportes sinteticos armados segun la documentacion (no hay mando real)
            Check(Sony.Crc((byte)'1', "23456789"u8.ToArray(), 8) == 0xCBF43926, "crc32");
            var d4 = new byte[64]; d4[0] = 1; d4[1] = 0; d4[2] = 255; d4[3] = 128; d4[4] = 128; d4[5] = 0x28; d4[6] = 0x21; d4[7] = 0x03; d4[8] = 200; d4[9] = 7; d4[30] = 0x1B;
            var s4 = Sony.Parse(Model.DS4, d4, 64)!.Value; // hat 8 = suelto, cruz, L1 + Options, PS + panel tactil
            Check(s4.B.HasFlag(Btn.A) && !s4.B.HasFlag(Btn.Up) && s4.B.HasFlag(Btn.LB) && s4.B.HasFlag(Btn.Guide) && s4.B.HasFlag(Btn.Touch), "ds4 usb botones");
            Check(s4.LX == 0 && s4.LY == 255 && s4.LT == 200 && s4.RT == 7 && s4.Pct == 100 && s4.Charging == false, "ds4 usb ejes/bateria");
            var d4b = new byte[78]; d4b[0] = 0x11; d4b[3] = 10; d4b[7] = 0x00; d4b[8] = 0x00; d4b[32] = 0x05;
            var s4b = Sony.Parse(Model.DS4, d4b, 78)!.Value;
            Check(s4b.LX == 10 && s4b.B.HasFlag(Btn.Up) && s4b.Pct == 55, "ds4 bluetooth"); // hat 0 = arriba
            var d5 = new byte[64]; d5[0] = 1; d5[1] = 1; d5[5] = 99; d5[6] = 88; d5[8] = 0x02 | 0x40; d5[9] = 0x20; d5[53] = 0x1A;
            var s5 = Sony.Parse(Model.DualSense, d5, 64)!.Value;
            Check(s5.B.HasFlag(Btn.Right) && s5.B.HasFlag(Btn.B) && s5.B.HasFlag(Btn.Start) && s5.LT == 99 && s5.RT == 88 && s5.Pct == 100 && s5.Charging, "dualsense usb");
            var d5b = new byte[78]; d5b[0] = 0x31; d5b[2] = 77; d5b[6] = 200; d5b[9] = 0x28; d5b[54] = 0x03;
            var s5b = Sony.Parse(Model.DualSense, d5b, 78)!.Value;
            Check(s5b.LX == 77 && s5b.RT == 0 && s5b.B.HasFlag(Btn.A) && s5b.Pct == 35 && !s5b.Charging, "dualsense bluetooth");
            Check(Sony.Parse(Model.DS4, new byte[5], 5) is null && Sony.Parse(Model.DualSense, new byte[64], 64) is null, "sony reporte invalido");
            foreach (var (m, usb) in new[] { (Model.DS4, false), (Model.DualSense, false), (Model.DS4, true), (Model.DualSense, true) })
            {
                var o = Sony.Output(m, usb, usb ? 64 : 78, 200, 100);
                if (!usb) Check(BitConverter.ToUInt32(o, 74) == Sony.Crc(0xA2, o, 74), "crc salida " + m);
                Check(o.Contains((byte)200) && o.Contains((byte)100), "motores en la salida " + m);
            }
            Console.WriteLine("selftest OK");
        }
        catch (Exception e) { Console.WriteLine("selftest FALLO: " + e.Message); Environment.ExitCode = 1; }
    }
}
