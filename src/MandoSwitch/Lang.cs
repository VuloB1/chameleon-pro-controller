using System.Globalization;

enum Kind { Waiting, Calibrating, Connected }

// Lo que el hilo del mando publica; el texto se arma en la interfaz, en el idioma vigente
record Status(Kind K, string Via = "", int Pct = 0, bool Charging = false, bool Exp = false); // Pct < 0: sin dato de bateria; Exp: mando experimental

// Textos de la interfaz. Orden de cada fila: es, en, pt, ja, ko, zh (simplificado).
static class L
{
    static readonly string[] Codes = { "es", "en", "pt", "ja", "ko", "zh" };
    public static readonly string[] Names = { "Español", "English", "Português", "日本語", "한국어", "中文 (简体)" };
    public static string Code(int i) => Codes[i];
    public static int Current { get; private set; } = 1;

    // "auto" sigue el idioma de Windows; si no esta entre los soportados, ingles
    public static void Set(string code)
    {
        if (code == "auto") code = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        int i = Array.IndexOf(Codes, code);
        Current = i < 0 ? 1 : i;
    }

    public static readonly Dictionary<string, string[]> T = new()
    {
        ["labels"] = new[] { "Letras de Nintendo en A/B/X/Y", "Nintendo letters on A/B/X/Y", "Letras da Nintendo em A/B/X/Y",
            "A/B/X/YをNintendoの文字配置にする", "A/B/X/Y를 Nintendo 글자 배치로", "A/B/X/Y 使用任天堂字母布局" },
        ["capture"] = new[] { "Capture = captura del monitor activo", "Capture = screenshot of the active monitor", "Capture = captura do monitor ativo",
            "Captureボタン = アクティブなモニターのスクリーンショット", "Capture 버튼 = 활성 모니터 스크린샷", "Capture 键 = 截取活动显示器" },
        ["name"] = new[] { "Nombre del mando...", "Controller name...", "Nome do controle...",
            "コントローラー名...", "컨트롤러 이름...", "手柄名称..." },
        ["nameprompt"] = new[] { "Nombre que se muestra en la bandeja (hasta 20 letras):", "Name shown in the tray (up to 20 characters):",
            "Nome exibido na bandeja (até 20 caracteres):", "トレイに表示する名前（20文字まで）:", "트레이에 표시할 이름(최대 20자):", "托盘中显示的名称（最多 20 个字符）：" },
        ["dead"] = new[] { "Zona muerta de los sticks", "Stick dead zone", "Zona morta dos analógicos",
            "スティックのデッドゾーン", "스틱 데드존", "摇杆死区" },
        ["autostart"] = new[] { "Iniciar con Windows", "Start with Windows", "Iniciar com o Windows",
            "Windowsと同時に起動", "Windows 시작 시 실행", "开机时启动" },
        ["language"] = new[] { "Idioma", "Language", "Idioma", "言語", "언어", "语言" },
        ["update"] = new[] { "Nueva versión disponible: v{0}", "New version available: v{0}", "Nova versão disponível: v{0}",
            "新しいバージョンがあります: v{0}", "새 버전이 있습니다: v{0}", "有新版本可用：v{0}" },
        ["quit"] = new[] { "Salir", "Quit", "Sair", "終了", "종료", "退出" },
        ["waiting"] = new[] { "Esperando el mando...", "Waiting for the controller...", "Aguardando o controle...",
            "コントローラーを待っています...", "컨트롤러를 기다리는 중...", "正在等待手柄..." },
        ["calibrating"] = new[] { "Calibrando, no toques los sticks...", "Calibrating, don't touch the sticks...", "Calibrando, não toque nos analógicos...",
            "キャリブレーション中、スティックに触れないでください...", "보정 중입니다. 스틱을 건드리지 마세요...", "正在校准，请勿触碰摇杆..." },
        ["connected"] = new[] { "Conectado por {0} - batería {1} %", "Connected via {0} - battery {1} %", "Conectado via {0} - bateria {1} %",
            "{0}で接続中 - バッテリー {1} %", "{0} 연결됨 - 배터리 {1} %", "已通过 {0} 连接 - 电量 {1} %" },
        ["connectednb"] = new[] { "Conectado por {0}", "Connected via {0}", "Conectado via {0}", "{0}で接続中", "{0} 연결됨", "已通过 {0} 连接" },
        ["experimental"] = new[] { " (experimental)", " (experimental)", " (experimental)", "（実験的）", " (실험적)", "（实验性）" },
        ["charging"] = new[] { " (cargando)", " (charging)", " (carregando)", "（充電中）", " (충전 중)", "（充电中）" },
        ["nohid"] = new[] { " (sin HidHide)", " (no HidHide)", " (sem HidHide)", "（HidHideなし）", " (HidHide 없음)", "（无 HidHide）" },
        ["novigem"] = new[] { "No se pudo crear el mando virtual. Instala ViGEmBus.", "Could not create the virtual controller. Install ViGEmBus.",
            "Não foi possível criar o controle virtual. Instale o ViGEmBus.", "仮想コントローラーを作成できませんでした。ViGEmBusをインストールしてください。",
            "가상 컨트롤러를 만들 수 없습니다. ViGEmBus를 설치하세요.", "无法创建虚拟手柄。请安装 ViGEmBus。" },
    };

    public static string Get(string key) => T[key][Current];

    public static string StatusText(Status s, bool hidHide) => s.K switch
    {
        Kind.Calibrating => Get("calibrating"),
        Kind.Connected => (s.Pct < 0 ? string.Format(Get("connectednb"), s.Via) : string.Format(Get("connected"), s.Via, s.Pct))
            + (s.Charging ? Get("charging") : "") + (s.Exp ? Get("experimental") : "") + (hidHide ? "" : Get("nohid")),
        _ => Get("waiting"),
    };
}
