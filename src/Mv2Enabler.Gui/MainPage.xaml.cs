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
    private bool _analysisFailed;

    public MainPage()
    {
        InitializeComponent();
        LocalizationService.Initialize();
        SelectCurrentLanguage();
        ApplyLanguage();
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

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not ComboBoxItem { Tag: string language })
        {
            return;
        }

        LocalizationService.SetLanguage(language);
        ResultInfoBar.IsOpen = false;
        ApplyLanguage();
    }

    private async Task AnalyzeAsync()
    {
        SetBusy(true);
        _analysisFailed = false;
        ResultInfoBar.IsOpen = false;
        AnalysisStatusText.Text = T("AnalyzingSemantic");
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
            ChromeVersionText.Text = T("Version", result.Report.Version);
            ChromePathText.Text = result.Report.ChromePath;
            DetailsTextBox.Text = FormatReport(result.Report);

            if (result.Report.Success && result.Report.Target is not null)
            {
                AnalysisStatusText.Text = T("Ready");
                AnalysisStatusDot.Background = Brush(Colors.LimeGreen);
            }
            else
            {
                AnalysisStatusText.Text = T("NoSafeTarget");
                AnalysisStatusDot.Background = Brush(Colors.OrangeRed);
                ShowMessage(
                    InfoBarSeverity.Error,
                    T("CannotOpenChrome"),
                    T("UnsupportedChrome"));
            }
        }
        catch (Exception exception)
        {
            _installation = null;
            _analysis = null;
            _analysisFailed = true;
            AnalysisStatusText.Text = T("CannotAnalyze");
            AnalysisStatusDot.Background = Brush(Colors.OrangeRed);
            DetailsTextBox.Text = exception.ToString();
            ShowMessage(InfoBarSeverity.Error, T("AnalysisError"), exception.Message);
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

            var launchMessage = outcome.ProfileRepair.ExtensionsReEnabled > 0
                ? T("LaunchSuccessWithRepair", outcome.Launch.ProcessId, outcome.ProfileRepair.ExtensionsReEnabled)
                : T("LaunchSuccess", outcome.Launch.ProcessId);
            ShowMessage(
                InfoBarSeverity.Success,
                T("LaunchSuccessTitle"),
                launchMessage);
            DetailsTextBox.Text = FormatLaunch(outcome.ProfileRepair, outcome.Launch) + Environment.NewLine + Environment.NewLine + DetailsTextBox.Text;
        }
        catch (Exception exception)
        {
            DetailsTextBox.Text = exception + Environment.NewLine + Environment.NewLine + DetailsTextBox.Text;
            ShowMessage(
                InfoBarSeverity.Error,
                T("CannotOpenChrome"),
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
                ? T("ChromeRunning", processes.Length)
                : T("ChromeClosed");
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

    private string FriendlyError(Exception exception)
    {
        if (exception.Message.Contains("Chrome is already running", StringComparison.OrdinalIgnoreCase))
        {
            return T("CloseChrome");
        }

        return exception.Message;
    }

    private void SelectCurrentLanguage()
    {
        foreach (var item in LanguageComboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, LocalizationService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            {
                LanguageComboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void ApplyLanguage()
    {
        LanguageLabelText.Text = T("LanguageLabel");
        HeroTitleText.Text = T("HeroTitle");
        HeroSubtitleText.Text = T("HeroSubtitle");
        BrowserStatusHeaderText.Text = T("BrowserStatus");
        LaunchOptionsHeaderText.Text = T("LaunchOptions");
        OpenExtensionsCheckBox.Content = T("OpenExtensions");
        LaunchButtonText.Text = T("LaunchButton");
        RefreshButtonText.Text = T("Refresh");
        HintText.Text = T("Hint");
        TechnicalDetailsExpander.Header = T("TechnicalDetails");

        if (_busy)
        {
            AnalysisStatusText.Text = T("AnalyzingSemantic");
        }
        else if (_analysisFailed)
        {
            AnalysisStatusText.Text = T("CannotAnalyze");
        }
        else if (_analysis is null)
        {
            AnalysisStatusText.Text = T("CheckingChrome");
        }
        else
        {
            AnalysisStatusText.Text = _analysis.Success ? T("Ready") : T("NoSafeTarget");
        }

        if (_analysis is null)
        {
            ChromeVersionText.Text = T("VersionPlaceholder");
            if (!_analysisFailed)
            {
                ChromePathText.Text = T("FindingChrome");
                DetailsTextBox.Text = T("NoAnalysis");
            }
        }
        else
        {
            ChromeVersionText.Text = T("Version", _analysis.Version);
        }

        UpdateProcessState();
    }

    private static string T(string key, params object[] arguments) => LocalizationService.Text(key, arguments);

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
