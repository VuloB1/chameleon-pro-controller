using System.Diagnostics;
using System.Text.RegularExpressions;

// Oculta el mando fisico a todo menos a esta app (HidHide) para que solo exista el Xbox virtual.
static class Hide
{
    const string Cli = @"C:\Program Files\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe";
    public static readonly bool Available = File.Exists(Cli);

    static string Exec(params string[] a)
    {
        var psi = new ProcessStartInfo(Cli) { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var x in a) psi.ArgumentList.Add(x);
        using var p = Process.Start(psi)!;
        var o = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return o;
    }

    static string Unescape(string s) => s.Replace(@"\\", @"\");

    // HidHide pide la ruta de instancia, HidSharp da el symbolic link: se cruzan con --dev-all
    static string? InstancePath(string symlink)
    {
        var rx = new Regex("\"symbolicLink\"\\s*:\\s*\"(.*?)\"[\\s\\S]*?\"deviceInstancePath\"\\s*:\\s*\"(.*?)\"");
        foreach (Match m in rx.Matches(Exec("--dev-all")))
            if (Unescape(m.Groups[1].Value).Equals(symlink, StringComparison.OrdinalIgnoreCase))
                return Unescape(m.Groups[2].Value);
        return null;
    }

    // Se queda oculto mientras la app viva, tambien entre desconexiones: si se soltara al desconectar,
    // Steam abriria el mando al reconectar antes que nosotros y lo trataria con su Steam Input.
    static readonly HashSet<string> hidden = new();

    public static void Apply(string symlink)
    {
        if (!Available || InstancePath(symlink) is not { } inst) return;
        lock (hidden) if (!hidden.Add(inst)) return; // ya oculto en esta ejecucion
        Exec("--app-reg", Environment.ProcessPath!);
        Exec("--dev-hide", inst);
        Exec("--cloak-on");
    }

    public static void Release()
    {
        string[] all;
        lock (hidden) { all = hidden.ToArray(); hidden.Clear(); }
        foreach (var inst in all) Exec("--dev-unhide", inst);
    }

    // Salir desde la bandeja ya libera; esto cubre cerrar sesion o apagar con la app abierta
    public static void ReleaseOnExit()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Release();
        Microsoft.Win32.SystemEvents.SessionEnding += (_, _) => Release();
    }

    // Si la app anterior murio sin soltar el mando, quedaria oculto: devolverlo al arrancar
    public static void Cleanup()
    {
        if (!Available) return;
        foreach (Match m in Regex.Matches(Exec("--dev-list"), "\"(.*?)\""))
            if (m.Groups[1].Value.Contains("VID_057E", StringComparison.OrdinalIgnoreCase)
                || m.Groups[1].Value.Contains("VID&0002057E", StringComparison.OrdinalIgnoreCase))
                Exec("--dev-unhide", m.Groups[1].Value);
    }
}
