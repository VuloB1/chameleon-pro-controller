using HidSharp;

readonly record struct Frame(byte B3, byte B4, byte B5, int LX, int LY, int RX, int RY, byte Bat);

static class Pro
{
    public const int Vid = 0x057E, Pid = 0x2009;
    public static readonly byte[] Idle = { 0x00, 0x01, 0x40, 0x40 };
    // shortcut: vibracion solo on/off (preset que sentimos en pruebas), escalar amplitud si hace falta
    public static readonly byte[] Buzz = { 0xC2, 0xC8, 0x03, 0x72 };
    static byte counter;

    // Reporte 0x30: [3..5] botones, [6..8] stick izq, [9..11] stick der (12 bits por eje)
    public static Frame? Parse(byte[] r, int n)
    {
        if (n < 12 || r[0] != 0x30) return null;
        return new Frame(r[3], r[4], r[5],
            r[6] | (r[7] & 0xF) << 8, r[7] >> 4 | r[8] << 4,
            r[9] | (r[10] & 0xF) << 8, r[10] >> 4 | r[11] << 4, r[2]);
    }

    // Byte 2, nibble alto: nivel 0..4 en los bits altos (8=lleno, 6, 4, 2, 0=vacio) y bit bajo = cargando.
    // Solo 5 escalones: el mando no da el porcentaje exacto.
    public static (int pct, bool charging) Battery(byte b)
    {
        int nibble = b >> 4;
        return (new[] { 5, 25, 50, 75, 100 }[Math.Min(nibble >> 1, 4)], (nibble & 1) != 0);
    }

    static void Write(HidStream s, int outLen, byte id, byte[] rumble, byte sub, params byte[] args)
    {
        var b = new byte[outLen];
        b[0] = id; b[1] = (byte)(counter++ & 0xF);
        Array.Copy(rumble, 0, b, 2, 4); Array.Copy(rumble, 0, b, 6, 4);
        if (id == 0x01) { b[10] = sub; Array.Copy(args, 0, b, 11, args.Length); }
        s.Write(b);
    }

    public static void Rumble(HidStream s, int outLen, byte[] rumble) => Write(s, outLen, 0x10, rumble, 0);

    public static void Init(HidStream s, int outLen, bool usb)
    {
        if (usb)
            foreach (byte cmd in new byte[] { 0x02, 0x04 }) // handshake + sin timeout
            {
                var h = new byte[outLen]; h[0] = 0x80; h[1] = cmd; s.Write(h); Thread.Sleep(60);
            }
        Write(s, outLen, 0x01, Idle, 0x03, 0x30); Thread.Sleep(60); // modo completo
        Write(s, outLen, 0x01, Idle, 0x30, 0x01); Thread.Sleep(60); // LED jugador 1
        Write(s, outLen, 0x01, Idle, 0x48, 0x01); Thread.Sleep(60); // activar vibracion
    }
}

// El mando clon trae calibracion ideal (centro 2048, rango 2047) que no es real: medimos el centro al conectar.
sealed class Stick
{
    public double Cx = 2048, Cy = 2048;
    // shortcut: rango inicial tipico y crece con lo observado (tope 2047); mejorar con asistente de calibracion
    public double Range = 1500;
    public static double Dead = 0.08;

    public (double x, double y) Norm(int rx, int ry)
    {
        double dx = rx - Cx, dy = ry - Cy;
        Range = Math.Max(Range, Math.Min(Math.Max(Math.Abs(dx), Math.Abs(dy)), 2047));
        double x = dx / Range, y = dy / Range, m = Math.Sqrt(x * x + y * y);
        if (m <= Dead) return (0, 0);
        double k = Math.Min(1, (m - Dead) / (1 - Dead)) / m;
        return (x * k, y * k);
    }
}
