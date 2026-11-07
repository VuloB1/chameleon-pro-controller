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
            MessageBox.Show($"No se pudo crear el mando virtual. Instala ViGEmBus.\n\n{e.Message}", "Chameleon",
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
    readonly ToolStripMenuItem status = new("Iniciando...") { Enabled = false };
    Icon? shown;

    public TrayContext()
    {
        Log.Reset();
        Stick.Dead = cfg.Dead;
        bridge = new Bridge(cfg, cts.Token);
        worker = new Thread(bridge.Run) { IsBackground = true, Name = "bridge", Priority = ThreadPriority.AboveNormal }; // que un juego al 100 % de CPU no retrase el mando
        worker.Start();

        var menu = new ContextMenuStrip();
        menu.Items.Add(status);
        menu.Items.Add(new ToolStripSeparator());

        var labels = new ToolStripMenuItem("Letras de Nintendo en A/B/X/Y") { Checked = cfg.Labels, CheckOnClick = true };
        labels.CheckedChanged += (_, _) => { cfg.Labels = labels.Checked; cfg.Save(); };
        menu.Items.Add(labels);

        var capture = new ToolStripMenuItem("Capture = captura del monitor activo") { Checked = cfg.Capture, CheckOnClick = true };
        capture.CheckedChanged += (_, _) => { cfg.Capture = capture.Checked; cfg.Save(); };
        menu.Items.Add(capture);

        menu.Items.Add("Nombre del mando...", null, (_, _) =>
        {
            var n = Microsoft.VisualBasic.Interaction.InputBox("Nombre que se muestra en la bandeja (hasta 20 letras):", "Chameleon", cfg.Name).Trim();
            if (n.Length == 0) return; // cancelar o dejarlo vacio: se queda el actual
            cfg.Name = n.Length > 20 ? n[..20] : n; cfg.Save(); Refresh();
        });

        var dead = new ToolStripMenuItem("Zona muerta de los sticks");
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

        var auto = new ToolStripMenuItem("Iniciar con Windows") { Checked = Settings.Autostart, CheckOnClick = true };
        auto.CheckedChanged += (_, _) => Settings.Autostart = auto.Checked;
        menu.Items.Add(auto);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => Quit());
        tray.ContextMenuStrip = menu;

        // El hilo del puente solo publica estado; la interfaz lo lee aqui, sin cruzar hilos
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        Refresh();
    }

    void Refresh()
    {
        string text = bridge.Text;
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
            Console.WriteLine("selftest OK");
        }
        catch (Exception e) { Console.WriteLine("selftest FALLO: " + e.Message); Environment.ExitCode = 1; }
    }
}
