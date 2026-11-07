; Instalador de Chameleon. Compilar con: ISCC.exe MandoSwitch.iss (necesita ..\dist\Chameleon.exe y deps\)
#define AppName "Chameleon"
#define AppVersion "1.0.0"
#define ViGEm "ViGEmBus_1.22.0_x64_x86_arm64.exe"
#define HidHide "HidHide_1.5.230_x64.exe"

[Setup]
AppId={{B3C1D2E4-6A7B-4C58-9D10-5E2F7A8B9C01}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DisableProgramGroupPage=yes
OutputDir=out
OutputBaseFilename=Chameleon-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
; los drivers se instalan en todo el sistema: hace falta administrador
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
WizardStyle=modern
UninstallDisplayIcon={app}\Chameleon.exe
SetupIconFile=..\src\MandoSwitch\icons\app.ico
; la app esta abierta si existe este mutex (ver Program.cs); el segundo es el de la version anterior, "Mando Switch"
AppMutex=Chameleon.Single,MandoSwitch.Single

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "autostart"; Description: "Iniciar Chameleon con Windows"

[InstallDelete]
; restos de la version anterior (se llamaba Mando Switch)
Type: files; Name: "{app}\MandoSwitch.exe"
Type: files; Name: "{autoprograms}\Mando Switch.lnk"

[Files]
Source: "..\dist\Chameleon.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "deps\{#ViGEm}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedViGEm
Source: "deps\{#HidHide}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: NeedHidHide

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\Chameleon.exe"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Chameleon"; \
    ValueData: """{app}\Chameleon.exe"""; Tasks: autostart

[Run]
; instaladores de Nefarius (Advanced Installer): /exenoui /qn = sin interfaz
Filename: "{tmp}\{#ViGEm}"; Parameters: "/exenoui /qn /norestart"; StatusMsg: "Instalando ViGEmBus (mando Xbox virtual)..."; \
    Flags: waituntilterminated; Check: NeedViGEm
Filename: "{tmp}\{#HidHide}"; Parameters: "/exenoui /qn /norestart"; StatusMsg: "Instalando HidHide (ocultar el mando fisico)..."; \
    Flags: waituntilterminated; Check: NeedHidHide
Filename: "{app}\Chameleon.exe"; Description: "Abrir Chameleon"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
; si la app murio sin soltar el mando, devolverlo; y quitar la app de la lista de HidHide
Filename: "{app}\Chameleon.exe"; Parameters: "--cleanup"; Flags: runhidden waituntilterminated; RunOnceId: "Cleanup"
Filename: "{commonpf}\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe"; Parameters: "--app-unreg ""{app}\Chameleon.exe"""; \
    Flags: runhidden waituntilterminated; RunOnceId: "UnregHidHide"; Check: HidHideInstalled

[Code]
function HidHideInstalled: Boolean;
begin
  Result := FileExists(ExpandConstant('{commonpf}\Nefarius Software Solutions\HidHide\x64\HidHideCLI.exe'));
end;

function NeedHidHide: Boolean;
begin
  Result := not HidHideInstalled;
end;

// Un ViGEmBus ya instalado (aunque sea antiguo) se respeta
function NeedViGEm: Boolean;
begin
  Result := not RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

// la entrada de inicio automatico tambien se crea desde el menu de la app: borrarla siempre al desinstalar
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Chameleon');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'MandoSwitch');
  end;
end;
