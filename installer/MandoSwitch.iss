; Instalador de Chameleon. Compilar con: ISCC.exe MandoSwitch.iss (necesita ..\dist\Chameleon.exe y deps\)
#define AppName "Chameleon"
#define AppVersion "1.1.0"
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
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
; no viene con todas las versiones de Inno Setup: se incluye una copia del repositorio oficial
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"

[CustomMessages]
english.AutoStart=Start Chameleon with Windows
spanish.AutoStart=Iniciar Chameleon con Windows
brazilianportuguese.AutoStart=Iniciar o Chameleon com o Windows
japanese.AutoStart=Windows の起動時に Chameleon を開始する
korean.AutoStart=Windows 시작 시 Chameleon 실행
chinesesimplified.AutoStart=开机时启动 Chameleon
chinesesimplified.OpenApp=打开 Chameleon
chinesesimplified.InstallingViGEm=正在安装 ViGEmBus（虚拟 Xbox 手柄）...
chinesesimplified.InstallingHidHide=正在安装 HidHide（隐藏物理手柄）...
english.OpenApp=Open Chameleon
spanish.OpenApp=Abrir Chameleon
brazilianportuguese.OpenApp=Abrir o Chameleon
japanese.OpenApp=Chameleon を開く
korean.OpenApp=Chameleon 열기
english.InstallingViGEm=Installing ViGEmBus (virtual Xbox controller)...
spanish.InstallingViGEm=Instalando ViGEmBus (mando Xbox virtual)...
brazilianportuguese.InstallingViGEm=Instalando o ViGEmBus (controle Xbox virtual)...
japanese.InstallingViGEm=ViGEmBus（仮想 Xbox コントローラー）をインストールしています...
korean.InstallingViGEm=ViGEmBus(가상 Xbox 컨트롤러) 설치 중...
english.InstallingHidHide=Installing HidHide (hides the physical controller)...
spanish.InstallingHidHide=Instalando HidHide (ocultar el mando físico)...
brazilianportuguese.InstallingHidHide=Instalando o HidHide (oculta o controle físico)...
japanese.InstallingHidHide=HidHide（物理コントローラーを隠す）をインストールしています...
korean.InstallingHidHide=HidHide(실제 컨트롤러 숨기기) 설치 중...

[Tasks]
Name: "autostart"; Description: "{cm:AutoStart}"

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
Filename: "{tmp}\{#ViGEm}"; Parameters: "/exenoui /qn /norestart"; StatusMsg: "{cm:InstallingViGEm}"; \
    Flags: waituntilterminated; Check: NeedViGEm
Filename: "{tmp}\{#HidHide}"; Parameters: "/exenoui /qn /norestart"; StatusMsg: "{cm:InstallingHidHide}"; \
    Flags: waituntilterminated; Check: NeedHidHide
Filename: "{app}\Chameleon.exe"; Description: "{cm:OpenApp}"; Flags: nowait postinstall skipifsilent runasoriginaluser

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
