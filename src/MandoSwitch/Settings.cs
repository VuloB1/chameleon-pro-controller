using System.Text.Json;
using Microsoft.Win32;

sealed class Settings
{
    public bool Labels { get; set; }      // true: letras de Nintendo; false: posicion fisica de Xbox
    public double Dead { get; set; } = 0.08;
    public string Name { get; set; } = "Chameleon"; // apodo que se muestra en la bandeja
    public string NotifiedVersion { get; set; } = "";
    public string Lang { get; set; } = "auto";     // "auto" = idioma de Windows, o es/en/pt/ja/ko/zh
    public bool Capture { get; set; } = true; // boton Capture = captura de pantalla de Windows (Win+ImprPant)

    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Chameleon");
    static readonly string File_ = Path.Combine(Dir, "settings.json");

    // Version anterior (se llamaba "Mando Switch"): conservar la configuracion y el inicio con Windows
    public static void MigrateFromMandoSwitch()
    {
        try
        {
            var old = Path.Combine(Path.GetDirectoryName(Dir)!, "MandoSwitch");
            if (Directory.Exists(old) && !Directory.Exists(Dir)) Directory.Move(old, Dir);
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (k?.GetValue("MandoSwitch") is not null) { k.DeleteValue("MandoSwitch"); Autostart = true; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(File_)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException) { return new(); } // sin archivo o corrupto: valores por defecto
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(File_, JsonSerializer.Serialize(this));
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool Autostart
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("Chameleon") is not null; }
        set
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, true)!;
            if (value) k.SetValue("Chameleon", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("Chameleon", false);
        }
    }
}

static class Log
{
    static readonly string F = Path.Combine(Settings.Dir, "log.txt");

    // Solo la sesion actual: el archivo se reinicia al arrancar
    public static void Reset()
    {
        try { Directory.CreateDirectory(Settings.Dir); File.WriteAllText(F, ""); } catch (IOException) { }
    }

    public static void Write(string msg)
    {
        try { File.AppendAllText(F, $"{DateTime.Now:HH:mm:ss} {msg}\n"); } catch (IOException) { }
    }
}
