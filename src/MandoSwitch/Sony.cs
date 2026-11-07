using HidSharp;

// EXPERIMENTAL: DualShock 4 y DualSense. Escrito desde la documentacion publica del protocolo (Linux hid-sony/hid-playstation),
// sin mando fisico para probar: los desplazamientos y el CRC estan cubiertos por la autoprueba, no por hardware real.
[Flags]
enum Btn : ushort
{
    A = 1, B = 2, X = 4, Y = 8, LB = 16, RB = 32, Back = 64, Start = 128, L3 = 256, R3 = 512,
    Guide = 1024, Up = 2048, Down = 4096, Left = 8192, Right = 16384, Touch = 32768,
}

enum Model { DS4, DualSense }

// Pct = -1 si el reporte no trae bateria
readonly record struct SFrame(Btn B, byte LT, byte RT, byte LX, byte LY, byte RX, byte RY, int Pct, bool Charging);

static class Sony
{
    public const int Vid = 0x054C;
    public static Model? ModelOf(int pid) => pid switch
    {
        0x05C4 or 0x09CC => Model.DS4,
        0x0CE6 or 0x0DF2 => Model.DualSense, // 0DF2 = DualSense Edge
        _ => null,
    };
    public static string Name(Model m) => m == Model.DS4 ? "DualShock 4" : "DualSense";

    static byte seq;

    public static SFrame? Parse(Model m, byte[] r, int n)
    {
        if (n < 10) return null;
        int o; bool ds5Full = false;
        if (m == Model.DS4) o = r[0] == 0x01 ? 1 : r[0] == 0x11 ? 3 : -1;
        else if (r[0] == 0x31) { o = 2; ds5Full = true; }
        else if (r[0] == 0x01) { o = 1; ds5Full = n >= 64; } // 0x01 corto por Bluetooth tiene la disposicion de DualShock 4
        else o = -1;
        if (o < 0) return null;

        int b1, b2, b3, lt, rt, batAt; byte lx = r[o], ly = r[o + 1], rx = r[o + 2], ry = r[o + 3];
        if (ds5Full) { lt = r[o + 4]; rt = r[o + 5]; b1 = r[o + 7]; b2 = r[o + 8]; b3 = r[o + 9]; batAt = o + 52; }
        else { b1 = r[o + 4]; b2 = r[o + 5]; b3 = r[o + 6]; lt = r[o + 7]; rt = r[o + 8]; batAt = o + 29; }

        Btn b = 0;
        int hat = b1 & 0x0F; // 0 = arriba y en sentido horario; 8 = suelto
        if (hat is 7 or 0 or 1) b |= Btn.Up;
        if (hat is 1 or 2 or 3) b |= Btn.Right;
        if (hat is 3 or 4 or 5) b |= Btn.Down;
        if (hat is 5 or 6 or 7) b |= Btn.Left;
        if ((b1 & 0x10) != 0) b |= Btn.X;      // cuadrado
        if ((b1 & 0x20) != 0) b |= Btn.A;      // cruz
        if ((b1 & 0x40) != 0) b |= Btn.B;      // circulo
        if ((b1 & 0x80) != 0) b |= Btn.Y;      // triangulo
        if ((b2 & 0x01) != 0) b |= Btn.LB;
        if ((b2 & 0x02) != 0) b |= Btn.RB;
        if ((b2 & 0x10) != 0) b |= Btn.Back;   // Share / Create
        if ((b2 & 0x20) != 0) b |= Btn.Start;  // Options
        if ((b2 & 0x40) != 0) b |= Btn.L3;
        if ((b2 & 0x80) != 0) b |= Btn.R3;
        if ((b3 & 0x01) != 0) b |= Btn.Guide;
        if ((b3 & 0x02) != 0) b |= Btn.Touch;

        int pct = -1; bool charging = false;
        if (n > batAt)
        {
            int v = r[batAt], level = v & 0x0F;
            if (m == Model.DS4) { charging = (v & 0x10) != 0 && level < 11; pct = Math.Min(level * 10 + 5, 100); }
            else { charging = v >> 4 == 1; pct = Math.Min(level * 10 + 5, 100); }
        }
        return new SFrame(b, (byte)lt, (byte)rt, lx, ly, rx, ry, pct, charging);
    }

    // Por Bluetooth los mandos arrancan en un reporte corto; leer este feature report los pasa al completo
    public static void Init(HidStream s, Model m, bool usb, int featLen)
    {
        if (usb || featLen <= 0) return;
        try { var f = new byte[featLen]; f[0] = m == Model.DS4 ? (byte)0x02 : (byte)0x05; s.GetFeature(f); }
        catch (Exception e) { Log.Write("Sony feature report: " + e.Message); }
    }

    public static byte[] Output(Model m, bool usb, int len, byte large, byte small)
    {
        var b = new byte[usb ? len : 78];
        if (m == Model.DS4)
        {
            if (usb) { b[0] = 0x05; b[1] = 0x07; b[4] = small; b[5] = large; b[6] = 53; b[7] = 196; b[8] = 107; } // barra de luz verde
            else { b[0] = 0x11; b[1] = 0xC4; b[3] = 0x07; b[6] = small; b[7] = large; b[8] = 53; b[9] = 196; b[10] = 107; }
        }
        else if (usb) { b[0] = 0x02; b[1] = 0x03; b[3] = small; b[4] = large; b[39] = 0x04; }
        else { b[0] = 0x31; b[1] = (byte)(seq++ << 4); b[2] = 0x10; b[3] = 0x03; b[5] = small; b[6] = large; b[41] = 0x04; }
        if (!usb) { uint c = Crc(0xA2, b, 74); b[74] = (byte)c; b[75] = (byte)(c >> 8); b[76] = (byte)(c >> 16); b[77] = (byte)(c >> 24); }
        return b;
    }

    // CRC-32 estandar; el prefijo (0xA2 en salida) cuenta como si fuera el primer byte
    public static uint Crc(byte seed, byte[] d, int len)
    {
        uint c = 0xFFFFFFFF;
        void Add(byte x) { c ^= x; for (int i = 0; i < 8; i++) c = (c & 1) != 0 ? c >> 1 ^ 0xEDB88320 : c >> 1; }
        Add(seed);
        for (int i = 0; i < len; i++) Add(d[i]);
        return ~c;
    }
}
