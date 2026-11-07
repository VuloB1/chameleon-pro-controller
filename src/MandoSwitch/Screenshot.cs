using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

// Captura solo el monitor donde esta la ventana activa (Win+ImprPant captura todos).
static class Screenshot
{
    [DllImport("user32")] static extern IntPtr GetForegroundWindow();

    // obturador sintetizado (incluido en el .exe); los sonidos de sistema suenan a aviso o error
    static readonly System.Media.SoundPlayer Shutter = new(
        new MemoryStream(ReadResource("shutter.wav")));

    static byte[] ReadResource(string name)
    {
        using var s = typeof(Screenshot).Assembly.GetManifestResourceStream(name)!;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return m.ToArray();
    }

    // shortcut: juegos en pantalla completa exclusiva pueden salir en negro; en modo ventana/sin bordes funciona
    public static void Take()
    {
        try
        {
            var area = Screen.FromHandle(GetForegroundWindow()).Bounds;
            using var bmp = new Bitmap(area.Width, area.Height);
            using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(area.Location, Point.Empty, area.Size);
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
            Directory.CreateDirectory(dir);
            bmp.Save(Path.Combine(dir, $"Chameleon {DateTime.Now:yyyy-MM-dd HH-mm-ss}.png"), ImageFormat.Png);
            Shutter.Play();
        }
        catch (Exception e) when (e is Win32Exception or IOException or ExternalException)
        {
            Log.Write($"Captura fallida: {e.Message}"); // p. ej. pantalla bloqueada
        }
    }
}
