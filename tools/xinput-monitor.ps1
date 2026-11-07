# Imprime los cambios del primer mando XInput (botones, gatillos, sticks) durante N segundos.
param([int]$Seconds = 60)
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class XI { [StructLayout(LayoutKind.Sequential)] public struct S { public uint Pkt; public ushort Btn; public byte LT, RT; public short LX, LY, RX, RY; }
 [DllImport("xinput1_4.dll")] public static extern int XInputGetState(int i, out S s); }
'@
$names = @{0x1='DPadUp';0x2='DPadDown';0x4='DPadLeft';0x8='DPadRight';0x10='Start';0x20='Back';0x40='LThumb';0x80='RThumb';0x100='LB';0x200='RB';0x400='Guide';0x1000='A';0x2000='B';0x4000='X';0x8000='Y'}
$end = (Get-Date).AddSeconds($Seconds); $lastBtn = 0; $lastKey = ''
while ((Get-Date) -lt $end) {
  $s = New-Object XI+S
  if ([XI]::XInputGetState(0, [ref]$s) -eq 0) {
    $pressed = ($names.Keys | Where-Object { $s.Btn -band $_ } | ForEach-Object { $names[$_] } | Sort-Object) -join '+'
    $key = "$pressed LT=$($s.LT) RT=$($s.RT) L=($([int]($s.LX/1000)),$([int]($s.LY/1000))) R=($([int]($s.RX/1000)),$([int]($s.RY/1000)))"
    if ($key -ne $lastKey) { "{0:HH:mm:ss.fff} {1}" -f (Get-Date), $key; $lastKey = $key }
  }
  Start-Sleep -Milliseconds 15
}
"fin"
