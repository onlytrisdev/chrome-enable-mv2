using System.Diagnostics;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mv2Enabler;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace Mv2Enabler_Gui;

/// <summary>
/// The main content page displayed inside the application window.
/// Add your UI logic, event handlers, and data binding here.
/// </summary>
public sealed partial class MainPage : Page
{
    private readonly DispatcherTimer _processTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ChromeInstallation? _installation;
    private AnalysisReport? _analysis;
    private bool _busy;

    public MainPage()
    {
        InitializeComponent();
        Loaded += MainPage_Loaded;
        Unloaded += MainPage_Unloaded;
        _processTimer.Tick += (_, _) => UpdateProcessState();
    }

    private async void MainPage_Loaded(object sender, RoutedEventArgs e)
    {
        _processTimer.Start();
        await AnalyzeAsync();
    }

    private void MainPage_Unloaded(object sender, RoutedEventArgs e) => _processTimer.Stop();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    private async Task AnalyzeAsync()
    {
        SetBusy(true);
        ResultInfoBar.IsOpen = false;
        AnalysisStatusText.Text = "Đang phân tích semantic signature…";
        AnalysisStatusDot.Background = Brush(Colors.Gray);

        try
        {
            var result = await Task.Run(() =>
            {
                var installation = ChromeInstallationFinder.Find();
                return (Installation: installation, Report: AnalysisService.Analyze(installation));
            });

            _installation = result.Installation;
            _analysis = result.Report;
            ChromeVersionText.Text = $"Phiên bản: {result.Report.Version}";
            ChromePathText.Text = result.Report.ChromePath;
            DetailsTextBox.Text = FormatReport(result.Report);

            if (result.Report.Success && result.Report.Target is not null)
            {
                AnalysisStatusText.Text = "Sẵn sàng — tìm thấy patch target an toàn";
                AnalysisStatusDot.Background = Brush(Colors.LimeGreen);
            }
            else
            {
                AnalysisStatusText.Text = "Không tìm thấy patch target an toàn";
                AnalysisStatusDot.Background = Brush(Colors.OrangeRed);
                ShowMessage(
                    InfoBarSeverity.Error,
                    "Không thể mở Chrome",
                    "Phiên bản Chrome này không khớp semantic signature. Công cụ đã dừng an toàn và không thay đổi gì.");
            }
        }
        catch (Exception exception)
        {
            _installation = null;
            _analysis = null;
            AnalysisStatusText.Text = "Không thể phân tích Chrome";
            AnalysisStatusDot.Background = Brush(Colors.OrangeRed);
            DetailsTextBox.Text = exception.ToString();
            ShowMessage(InfoBarSeverity.Error, "Lỗi phân tích", exception.Message);
        }
        finally
        {
            SetBusy(false);
            UpdateProcessState();
        }
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        if (_installation is null || _analysis?.Target is null || !_analysis.Success)
        {
            await AnalyzeAsync();
            return;
        }

        var installation = _installation;
        var target = _analysis.Target;
        IReadOnlyList<string> chromeArguments = OpenExtensionsCheckBox.IsChecked == true
            ? ["chrome://extensions/"]
            : [];

        SetBusy(true);
        ResultInfoBar.IsOpen = false;
        try
        {
            var outcome = await Task.Run(() =>
            {
                ChromeDebugLauncher.EnsureChromeIsNotRunning();
                var profileRepair = ChromeProfileRepair.RepairBeforeLaunch(installation, chromeArguments);
                var launch = ChromeDebugLauncher.Launch(
                    installation,
                    target,
                    chromeArguments,
                    TimeSpan.FromSeconds(20));
                return (ProfileRepair: profileRepair, Launch: launch);
            });

            var repairText = outcome.ProfileRepair.ExtensionsReEnabled > 0
                ? $" Đã tự khôi phục {outcome.ProfileRepair.ExtensionsReEnabled} extension MV2."
                : string.Empty;
            ShowMessage(
                InfoBarSeverity.Success,
                "Chrome đã được mở với Manifest V2",
                $"Patch RAM đã được xác minh trên PID {outcome.Launch.ProcessId}.{repairText}");
            DetailsTextBox.Text = FormatLaunch(outcome.ProfileRepair, outcome.Launch) + Environment.NewLine + Environment.NewLine + DetailsTextBox.Text;
        }
        catch (Exception exception)
        {
            DetailsTextBox.Text = exception + Environment.NewLine + Environment.NewLine + DetailsTextBox.Text;
            ShowMessage(
                InfoBarSeverity.Error,
                "Không thể mở Chrome",
                FriendlyError(exception));
        }
        finally
        {
            SetBusy(false);
            UpdateProcessState();
        }
    }

    private void UpdateProcessState()
    {
        var processes = Process.GetProcessesByName("chrome");
        try
        {
            var running = processes.Length > 0;
            ProcessStatusText.Text = running
                ? $"Chrome đang chạy ({processes.Length} tiến trình)"
                : "Chrome đã đóng — có thể launch";
            ProcessStatusDot.Background = Brush(running ? Colors.Gold : Colors.LimeGreen);
            LaunchButton.IsEnabled = !_busy && !running && _analysis?.Success == true && _analysis.Target is not null;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        BusyRing.IsActive = busy;
        if (busy)
        {
            LaunchButton.IsEnabled = false;
        }
    }

    private void ShowMessage(InfoBarSeverity severity, string title, string message)
    {
        ResultInfoBar.Severity = severity;
        ResultInfoBar.Title = title;
        ResultInfoBar.Message = message;
        ResultInfoBar.IsOpen = true;
    }

    private static string FriendlyError(Exception exception)
    {
        if (exception.Message.Contains("Chrome is already running", StringComparison.OrdinalIgnoreCase))
        {
            return "Hãy đóng hoàn toàn mọi cửa sổ Chrome. Nút Launch sẽ tự bật lại sau vài giây.";
        }

        return exception.Message;
    }

    private static SolidColorBrush Brush(Windows.UI.Color color) => new(color);

    private static string FormatReport(AnalysisReport report)
    {
        var builder = new StringBuilder();
        builder.AppendLine(report.Success ? "ANALYSIS: SAFE TARGET FOUND" : "ANALYSIS: NO SAFE TARGET");
        builder.AppendLine($"Chrome:  {report.ChromePath}");
        builder.AppendLine($"DLL:     {report.DllPath}");
        builder.AppendLine($"Version: {report.Version}");
        builder.AppendLine($"SHA-256: {report.Sha256}");
        builder.AppendLine($"Matches: pattern={report.PatternMatchCount}, semantic={report.SemanticMatchCount}");
        if (report.Target is { } target)
        {
            builder.AppendLine($"Rule:    {target.RuleId}");
            builder.AppendLine($"Edits:   {target.AdditionalEdits.Count + 1} verified RAM bytes");
            foreach (var evidence in target.Evidence)
            {
                builder.AppendLine($"  ✓ {evidence}");
            }
        }

        foreach (var diagnostic in report.Diagnostics)
        {
            builder.AppendLine($"  - {diagnostic}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string FormatLaunch(ProfileRepairResult repair, LaunchResult launch)
    {
        var builder = new StringBuilder();
        builder.AppendLine("LAUNCH: SUCCESS");
        builder.AppendLine($"PID: {launch.ProcessId}");
        builder.AppendLine($"Remote address: {launch.RemoteAddress}");
        builder.AppendLine($"Primary byte: 0x{launch.OriginalByte:X2} -> 0x{launch.ReplacementByte:X2}");
        builder.AppendLine($"Profiles scanned: {repair.ProfilesScanned}");
        builder.AppendLine($"Extensions re-enabled: {repair.ExtensionsReEnabled}");
        builder.AppendLine("On-disk chrome.dll was not modified.");
        return builder.ToString().TrimEnd();
    }
}
