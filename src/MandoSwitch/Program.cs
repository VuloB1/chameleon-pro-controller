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
            Console.WriteLine("selftest OK");
        }
        catch (Exception e) { Console.WriteLine("selftest FALLO: " + e.Message); Environment.ExitCode = 1; }
    }
}
