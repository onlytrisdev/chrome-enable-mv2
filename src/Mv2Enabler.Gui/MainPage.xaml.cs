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
    private ReleaseUpdate? _availableUpdate;
    private bool _busy;
    private bool _analysisFailed;
    private bool _checkingUpdate;
    private bool _installingUpdate;

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
        _ = CheckUpdateAsync(showCurrentStatus: false);
        if (App.AutoLaunchRequested)
        {
            OpenExtensionsCheckBox.IsChecked = false;
        }

        await AnalyzeAsync();
        if (App.AutoLaunchRequested)
        {
            if (_analysis?.Success == true)
            {
                await LaunchAsync();
            }
            else
            {
                ShowMessage(InfoBarSeverity.Warning, T("AutoLaunchUnavailable"), T("UnsupportedChrome"));
            }
        }
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

    private async void LaunchButton_Click(object sender, RoutedEventArgs e) => await LaunchAsync();

    private async Task LaunchAsync()
    {
        if (_installation is null || _analysis?.Target is null || !_analysis.Success)
        {
            await AnalyzeAsync();
            return;
        }

        var installation = _installation;
        var target = _analysis.Target;
        IReadOnlyList<string> chromeArguments = OpenExtensionsCheckBox.IsChecked == true
            ? [.. App.ChromeArguments, "chrome://extensions/"]
            : App.ChromeArguments;

        SetBusy(true);
        ResultInfoBar.IsOpen = false;
        var launched = false;
        try
        {
            await Task.Run(() =>
            {
                ChromeDebugLauncher.EnsureChromeIsNotRunning();
                ChromeProfileRepair.RepairBeforeLaunch(installation, chromeArguments);
                ChromeDebugLauncher.Launch(
                    installation,
                    target,
                    chromeArguments,
                    TimeSpan.FromSeconds(20));
            });
            launched = true;
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

        if (launched)
        {
            Application.Current.Exit();
        }
    }

    private async void CreateShortcutButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The launcher path is unavailable.");
            var path = await Task.Run(() => DesktopShortcutService.CreateForLauncher(executable));
            ShowMessage(InfoBarSeverity.Success, T("ShortcutCreated"), path);
        }
        catch (Exception exception)
        {
            ShowMessage(InfoBarSeverity.Error, T("ShortcutError"), exception.Message);
        }
    }

    private void OpenChromeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var chrome = _installation?.ExecutablePath ?? ChromeInstallationFinder.Find().ExecutablePath;
            var startInfo = new ProcessStartInfo(chrome) { UseShellExecute = true };
            foreach (var argument in App.ChromeArguments)
            {
                startInfo.ArgumentList.Add(argument);
            }
            Process.Start(startInfo);
            Application.Current.Exit();
        }
        catch (Exception exception)
        {
            ShowMessage(InfoBarSeverity.Error, T("CannotOpenChrome"), exception.Message);
        }
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e) =>
        await CheckUpdateAsync(showCurrentStatus: true);

    private async Task CheckUpdateAsync(bool showCurrentStatus)
    {
        if (_checkingUpdate || _installingUpdate)
        {
            return;
        }

        _checkingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = T("CheckingUpdates");
        try
        {
            var version = typeof(App).Assembly.GetName().Version
                ?? throw new InvalidOperationException("The launcher version is unavailable.");
            _availableUpdate = await ReleaseUpdateService.CheckAsync(version);
            InstallUpdateButton.Visibility = _availableUpdate is null ? Visibility.Collapsed : Visibility.Visible;
            UpdateStatusText.Text = _availableUpdate is null
                ? (showCurrentStatus ? T("UpToDate") : string.Empty)
                : T("UpdateAvailable", _availableUpdate.Version);
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = showCurrentStatus ? T("UpdateCheckFailed", exception.Message) : string.Empty;
        }
        finally
        {
            _checkingUpdate = false;
            CheckUpdateButton.IsEnabled = true;
        }
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is null || _installingUpdate)
        {
            return;
        }

        _installingUpdate = true;
        InstallUpdateButton.IsEnabled = false;
        CheckUpdateButton.IsEnabled = false;
        UpdateStatusText.Text = T("InstallingUpdate", _availableUpdate.Version);
        try
        {
            var executable = await ReleaseUpdateService.InstallAsync(_availableUpdate);
            await Task.Run(() => DesktopShortcutService.CreateForLauncher(executable));
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
            Application.Current.Exit();
        }
        catch (Exception exception)
        {
            UpdateStatusText.Text = T("UpdateInstallFailed", exception.Message);
        }
        finally
        {
            _installingUpdate = false;
            InstallUpdateButton.IsEnabled = true;
            CheckUpdateButton.IsEnabled = true;
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
            LaunchButton.IsEnabled = !_busy && !_installingUpdate && !running &&
                _analysis?.Success == true && _analysis.Target is not null;
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
        CreateShortcutButtonText.Text = T("CreateShortcut");
        OpenChromeButtonText.Text = T("OpenChromeNormally");
        CheckUpdateButtonText.Text = T("CheckUpdates");
        InstallUpdateButtonText.Text = T("InstallUpdate");
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

}
