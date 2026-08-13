using System.Globalization;

namespace Mv2Enabler_Gui;

internal static class LocalizationService
{
    private const string Vietnamese = "vi";
    private const string English = "en";
    private const string SimplifiedChinese = "zh-CN";
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ChromeMv2Launcher",
        "language.txt");

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Translations =
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Vietnamese] = new Dictionary<string, string>
            {
                ["LanguageLabel"] = "Ngôn ngữ",
                ["HeroTitle"] = "Giữ Manifest V2 hoạt động",
                ["HeroSubtitle"] = "Tự kiểm tra Chrome, khôi phục extension MV2 đã cài và áp dụng patch an toàn trong RAM.",
                ["CheckingChrome"] = "Đang kiểm tra Chrome…",
                ["VersionPlaceholder"] = "Phiên bản: —",
                ["Version"] = "Phiên bản: {0}",
                ["FindingChrome"] = "Đang tìm chrome.exe…",
                ["BrowserStatus"] = "Trạng thái trình duyệt",
                ["LaunchOptions"] = "Cách mở",
                ["OpenExtensions"] = "Mở trang quản lý tiện ích",
                ["LaunchButton"] = "Mở Chrome với Manifest V2",
                ["Refresh"] = "Kiểm tra lại",
                ["Hint"] = "Extension chỉ cần cài một lần. Các phiên Chrome sau phải được mở từ ứng dụng này.",
                ["TechnicalDetails"] = "Chi tiết kỹ thuật",
                ["NoAnalysis"] = "Chưa có dữ liệu phân tích.",
                ["AnalyzingSemantic"] = "Đang phân tích semantic signature…",
                ["Ready"] = "Sẵn sàng — tìm thấy patch target an toàn",
                ["NoSafeTarget"] = "Không tìm thấy patch target an toàn",
                ["CannotOpenChrome"] = "Không thể mở Chrome",
                ["UnsupportedChrome"] = "Phiên bản Chrome này không khớp semantic signature. Công cụ đã dừng an toàn và không thay đổi gì.",
                ["CannotAnalyze"] = "Không thể phân tích Chrome",
                ["AnalysisError"] = "Lỗi phân tích",
                ["LaunchSuccessTitle"] = "Chrome đã được mở với Manifest V2",
                ["LaunchSuccess"] = "Patch RAM đã được xác minh trên PID {0}.",
                ["LaunchSuccessWithRepair"] = "Patch RAM đã được xác minh trên PID {0}. Đã tự khôi phục {1} extension MV2.",
                ["ChromeRunning"] = "Chrome đang chạy ({0} tiến trình)",
                ["ChromeClosed"] = "Chrome đã đóng — có thể launch",
                ["CloseChrome"] = "Hãy đóng hoàn toàn mọi cửa sổ Chrome. Nút Launch sẽ tự bật lại sau vài giây."
            },
            [English] = new Dictionary<string, string>
            {
                ["LanguageLabel"] = "Language",
                ["HeroTitle"] = "Keep Manifest V2 working",
                ["HeroSubtitle"] = "Automatically check Chrome, restore installed MV2 extensions, and safely apply the patch in memory.",
                ["CheckingChrome"] = "Checking Chrome…",
                ["VersionPlaceholder"] = "Version: —",
                ["Version"] = "Version: {0}",
                ["FindingChrome"] = "Looking for chrome.exe…",
                ["BrowserStatus"] = "Browser status",
                ["LaunchOptions"] = "Launch options",
                ["OpenExtensions"] = "Open the extensions manager",
                ["LaunchButton"] = "Launch Chrome with Manifest V2",
                ["Refresh"] = "Check again",
                ["Hint"] = "Install an extension only once. Future Chrome sessions must be opened from this app.",
                ["TechnicalDetails"] = "Technical details",
                ["NoAnalysis"] = "No analysis data yet.",
                ["AnalyzingSemantic"] = "Analyzing semantic signatures…",
                ["Ready"] = "Ready — a safe patch target was found",
                ["NoSafeTarget"] = "No safe patch target was found",
                ["CannotOpenChrome"] = "Chrome could not be launched",
                ["UnsupportedChrome"] = "This Chrome build does not match the semantic signature. The tool stopped safely without changing anything.",
                ["CannotAnalyze"] = "Chrome could not be analyzed",
                ["AnalysisError"] = "Analysis error",
                ["LaunchSuccessTitle"] = "Chrome launched with Manifest V2",
                ["LaunchSuccess"] = "The RAM patch was verified on PID {0}.",
                ["LaunchSuccessWithRepair"] = "The RAM patch was verified on PID {0}. Automatically restored {1} MV2 extension(s).",
                ["ChromeRunning"] = "Chrome is running ({0} processes)",
                ["ChromeClosed"] = "Chrome is closed — ready to launch",
                ["CloseChrome"] = "Close every Chrome window completely. The Launch button will become available again in a few seconds."
            },
            [SimplifiedChinese] = new Dictionary<string, string>
            {
                ["LanguageLabel"] = "语言",
                ["HeroTitle"] = "让 Manifest V2 保持可用",
                ["HeroSubtitle"] = "自动检测 Chrome、恢复已安装的 MV2 扩展，并在内存中安全应用补丁。",
                ["CheckingChrome"] = "正在检测 Chrome…",
                ["VersionPlaceholder"] = "版本：—",
                ["Version"] = "版本：{0}",
                ["FindingChrome"] = "正在查找 chrome.exe…",
                ["BrowserStatus"] = "浏览器状态",
                ["LaunchOptions"] = "启动选项",
                ["OpenExtensions"] = "打开扩展程序管理页面",
                ["LaunchButton"] = "使用 Manifest V2 启动 Chrome",
                ["Refresh"] = "重新检测",
                ["Hint"] = "扩展只需安装一次。之后的 Chrome 会话必须从此应用启动。",
                ["TechnicalDetails"] = "技术详情",
                ["NoAnalysis"] = "暂无分析数据。",
                ["AnalyzingSemantic"] = "正在分析语义特征…",
                ["Ready"] = "已就绪 — 找到安全的补丁目标",
                ["NoSafeTarget"] = "未找到安全的补丁目标",
                ["CannotOpenChrome"] = "无法启动 Chrome",
                ["UnsupportedChrome"] = "此 Chrome 版本与语义特征不匹配。工具已安全停止，未进行任何更改。",
                ["CannotAnalyze"] = "无法分析 Chrome",
                ["AnalysisError"] = "分析错误",
                ["LaunchSuccessTitle"] = "Chrome 已使用 Manifest V2 启动",
                ["LaunchSuccess"] = "已在 PID {0} 上验证内存补丁。",
                ["LaunchSuccessWithRepair"] = "已在 PID {0} 上验证内存补丁。已自动恢复 {1} 个 MV2 扩展。",
                ["ChromeRunning"] = "Chrome 正在运行（{0} 个进程）",
                ["ChromeClosed"] = "Chrome 已关闭 — 可以启动",
                ["CloseChrome"] = "请完全关闭所有 Chrome 窗口。启动按钮将在几秒后重新启用。"
            }
        };

    public static string CurrentLanguage { get; private set; } = English;

    public static void Initialize()
    {
        var saved = TryReadSavedLanguage();
        CurrentLanguage = NormalizeLanguage(saved ?? CultureInfo.CurrentUICulture.Name);
    }

    public static void SetLanguage(string language)
    {
        CurrentLanguage = NormalizeLanguage(language);
        try
        {
            var directory = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(SettingsPath, CurrentLanguage);
        }
        catch (IOException)
        {
            // Language selection still applies for the current session.
        }
        catch (UnauthorizedAccessException)
        {
            // Language selection still applies for the current session.
        }
    }

    public static string Text(string key, params object[] arguments)
    {
        var language = Translations.TryGetValue(CurrentLanguage, out var selected)
            ? selected
            : Translations[English];
        var value = language.TryGetValue(key, out var translated)
            ? translated
            : Translations[English].GetValueOrDefault(key, key);
        return arguments.Length == 0
            ? value
            : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }

    private static string? TryReadSavedLanguage()
    {
        try
        {
            return File.Exists(SettingsPath) ? File.ReadAllText(SettingsPath).Trim() : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string NormalizeLanguage(string language)
    {
        if (language.StartsWith("vi", StringComparison.OrdinalIgnoreCase))
        {
            return Vietnamese;
        }

        if (language.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return SimplifiedChinese;
        }

        return English;
    }
}
