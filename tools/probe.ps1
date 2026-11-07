# Fase 0: lista mandos HID Nintendo (VID 057E), muestra caps y lee reportes ~6 s (solo lectura, no envia nada).
Add-Type -TypeDefinition @'
using System; using System.Runtime.InteropServices; using System.Threading; using Microsoft.Win32.SafeHandles; using System.Collections.Generic;
public static class Hid {
  [StructLayout(LayoutKind.Sequential)] struct SP { public int cb; public Guid g; public int f; public IntPtr r; }
  [StructLayout(LayoutKind.Sequential)] struct Attr { public int Size; public ushort Vid, Pid, Ver; }
  [StructLayout(LayoutKind.Sequential)] struct Caps { public ushort Usage, UsagePage, In, Out, Feat; [MarshalAs(UnmanagedType.ByValArray, SizeConst=17)] public ushort[] R; public ushort a,b,c; }
  [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
  [DllImport("hid.dll")] static extern bool HidD_GetAttributes(SafeFileHandle h, ref Attr a);
  [DllImport("hid.dll")] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr p);
  [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr p);
  [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr p, out Caps c);
  [DllImport("hid.dll", CharSet=CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, System.Text.StringBuilder b, int len);
  [DllImport("setupapi.dll")] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr p, int f);
  [DllImport("setupapi.dll")] static extern bool SetupDiEnumDeviceInterfaces(IntPtr s, IntPtr d, ref Guid g, int i, ref SP o);
  [DllImport("setupapi.dll", CharSet=CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr s, ref SP i, IntPtr d, int n, out int req, IntPtr di);
  [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] static extern SafeFileHandle CreateFile(string n, uint a, uint s, IntPtr sa, uint c, uint f, IntPtr t);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool ReadFile(SafeFileHandle h, byte[] b, int n, out int r, IntPtr o);
  [DllImport("kernel32.dll")] static extern bool CancelIoEx(SafeFileHandle h, IntPtr o);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool CancelSynchronousIo(IntPtr t);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool WriteFile(SafeFileHandle h, byte[] b, int n, out int w, IntPtr o);
  static byte cnt = 0; static int outLen = 49;
  static bool Send(SafeFileHandle h, byte[] rumble, byte sub, params byte[] args) {
    var b = new byte[outLen]; b[0] = (sub == 0xFF ? (byte)0x10 : (byte)0x01); b[1] = (byte)(cnt++ & 0xF);
    Array.Copy(rumble, 0, b, 2, 4); Array.Copy(rumble, 0, b, 6, 4);
    if (sub != 0xFF) { b[10] = sub; Array.Copy(args, 0, b, 11, args.Length); }
    int w; bool ok = WriteFile(h, b, b.Length, out w, IntPtr.Zero);
    if (!ok) Console.WriteLine("  WriteFile fallo, sub=0x{0:X2} err={1}", sub, Marshal.GetLastWin32Error());
    Thread.Sleep(120); return ok;
  }
  public static void Run(int seconds) {
    Guid g; HidD_GetHidGuid(out g);
    IntPtr set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12);
    for (int i = 0; ; i++) {
      var sp = new SP(); sp.cb = Marshal.SizeOf(sp);
      if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref sp)) break;
      int req; SetupDiGetDeviceInterfaceDetail(set, ref sp, IntPtr.Zero, 0, out req, IntPtr.Zero);
      IntPtr buf = Marshal.AllocHGlobal(req); Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
      SetupDiGetDeviceInterfaceDetail(set, ref sp, buf, req, out req, IntPtr.Zero);
      string path = Marshal.PtrToStringUni(buf + 4); Marshal.FreeHGlobal(buf);
      var h = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
      if (h.IsInvalid) continue;
      var a = new Attr(); a.Size = Marshal.SizeOf(a);
      if (!HidD_GetAttributes(h, ref a) || a.Vid != 0x057E) { h.Close(); continue; }
      IntPtr pp; HidD_GetPreparsedData(h, out pp); Caps c; HidP_GetCaps(pp, out c); HidD_FreePreparsedData(pp);
      outLen = c.Out;
      var sb = new System.Text.StringBuilder(128); HidD_GetProductString(h, sb, 256);
      Console.WriteLine("Dispositivo: {0}  VID={1:X4} PID={2:X4} ver={3:X4}", sb, a.Vid, a.Pid, a.Ver);
      Console.WriteLine("  Usage={0:X}/{1:X} InputLen={2} OutputLen={3} FeatureLen={4}", c.UsagePage, c.Usage, c.In, c.Out, c.Feat);
      Console.WriteLine("  Path: " + path);
      Console.WriteLine("  Mueve sticks / pulsa botones durante {0}s...", seconds);
      var seen = new Dictionary<byte,int>(); byte[] first = null; byte[] last = null; int total = 0;
      var replies = new List<byte[]>(); var done = false; var rb = new byte[Math.Max((int)c.In, 64)];
      var t = new Thread(() => { try { while (!done) { int n; if (!ReadFile(h, rb, rb.Length, out n, IntPtr.Zero) || n == 0) break;
        total++; if (!seen.ContainsKey(rb[0])) seen[rb[0]] = 0; seen[rb[0]]++; if (first == null) first = (byte[])rb.Clone();
        if (rb[0] == 0x21) lock (replies) replies.Add((byte[])rb.Clone()); else last = (byte[])rb.Clone(); } } catch {} });
      t.Start();
      var idle = new byte[] { 0x00, 0x01, 0x40, 0x40 };
      Send(h, idle, 0x03, 0x30);                 // modo completo 0x30
      Send(h, idle, 0x02);                       // info del dispositivo
      Send(h, idle, 0x10, 0x3D, 0x60, 0, 0, 0x19); // SPI 0x603D: calibracion sticks de fabrica
      Send(h, idle, 0x10, 0x10, 0x80, 0, 0, 0x16); // SPI 0x8010: calibracion de usuario
      Send(h, idle, 0x30, 0x01);                 // LED jugador 1
      Send(h, idle, 0x48, 0x01);                 // activar vibracion
      Console.WriteLine("  Vibracion en 1 s (deberias sentirla 0.5 s)...");
      var buzz = new byte[] { 0xC2, 0xC8, 0x03, 0x72 };
      for (int k = 0; k < 6; k++) Send(h, buzz, 0xFF);
      Send(h, idle, 0xFF);
      if (!t.Join(seconds * 1000)) { done = true; CancelSynchronousIo(IntPtr.Zero); CancelIoEx(h, IntPtr.Zero); t.Join(1000); }
      Console.WriteLine("  Reportes recibidos: " + total);
      foreach (var kv in seen) Console.WriteLine("    ID 0x{0:X2}: {1}", kv.Key, kv.Value);
      lock (replies) foreach (var r in replies) Console.WriteLine("  Respuesta 0x21 ack={0:X2} sub={1:X2} datos: {2}", r[13], r[14], BitConverter.ToString(r, 15, 28));
      if (replies.Count == 0) Console.WriteLine("  Sin respuestas a subcomandos (el clon los ignora?)");
      if (last != null) Console.WriteLine("  Ultimo reporte: " + BitConverter.ToString(last, 0, Math.Min(last.Length, 20)));
      h.Close();
    }
  }
}
'@
[Hid]::Run(6)
