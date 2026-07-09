using System.Net;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using ArcaDownloader.Core.Auth;
using ArcaDownloader.Core.Download;
using ArcaDownloader.Core.Models;
using ArcaDownloader.Core.Services;
using DirectN;
using WebView2;
using WebView2.Utilities;

if (args.Contains("--check-session", StringComparer.OrdinalIgnoreCase))
{
    var exitCode = await RunSessionCheckCliAsync(args);
    Environment.Exit(exitCode);
    return;
}

Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
Thread.CurrentThread.SetApartmentState(ApartmentState.STA);

try
{
    Win32Platform.Register();
    GdiBackend.Register();

    AppDomain.CurrentDomain.UnhandledException += (_, e) =>
    {
        if (e.ExceptionObject is Exception ex)
        {
            NativeMessageBox.Show(ex.ToString(), "Unhandled exception", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
        }
    };

    Application.DispatcherUnhandledException += e =>
    {
        NativeMessageBox.Show(e.Exception.ToString(), "UI exception", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
        e.Handled = true;
    };

    Application.Create()
        .UseAccent(Accent.Green)
        .BuildMainWindow(() => new MainWindow())
        .Run();
}
catch (Exception ex)
{
    NativeMessageBox.Show(ex.ToString(), "Fatal error", NativeMessageBoxButtons.Ok, NativeMessageBoxIcon.Error);
}

static async Task<int> RunSessionCheckCliAsync(string[] args)
{
    NativeMethods.AttachConsole(NativeMethods.AttachParentProcess);

    var logPath = GetSessionCheckLogPath(args);
    var lines = new List<string>
    {
        $"timestamp={DateTimeOffset.Now:O}",
        $"cookie_path={CookieJar.Default().Path}"
    };

    try
    {
        var cookieJar = CookieJar.Default();
        var cookies = await cookieJar.LoadAsync();
        var saved = CookieJar.FromContainer(cookies, new Uri("https://arca.live/"));
        lines.Add($"cookie_count={saved.Count}");
        var requestCookies = cookies.GetCookies(new Uri("https://arca.live/settings/profile")).Cast<Cookie>().ToList();
        var requestCookieHeader = cookies.GetCookieHeader(new Uri("https://arca.live/settings/profile"));
        lines.Add($"request_cookie_names={string.Join(",", requestCookies.Select(cookie => cookie.Name))}");
        lines.Add($"request_cookie_value_lengths={string.Join(",", requestCookies.Select(cookie => $"{cookie.Name}:{cookie.Value.Length}"))}");
        lines.Add($"request_cookie_header_length={requestCookieHeader.Length}");
        lines.Add($"request_cookie_header_names={string.Join(",", requestCookieHeader.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(part => part.Split('=', 2)[0]))}");

        if (saved.Count == 0)
        {
            lines.Add("valid=False");
            lines.Add("reason=No saved cookies");
            WriteSessionCheckLines(lines, logPath);
            return 2;
        }

        var plainResult = await CheckSessionWithPlainClientAsync(cookies);
        lines.Add($"plain_valid={plainResult.IsValid}");
        lines.Add($"plain_status_code={plainResult.StatusCode}");
        lines.Add($"plain_final_uri={plainResult.FinalUri ?? ""}");
        lines.Add($"plain_reason={plainResult.Reason}");

        var result = await ArcaSessionValidator.CheckSessionAsync(cookies);
        lines.Add($"valid={result.IsValid}");
        lines.Add($"status_code={result.StatusCode}");
        lines.Add($"final_uri={result.FinalUri ?? ""}");
        lines.Add($"has_forbidden_marker={result.HasForbiddenMarker}");
        lines.Add($"is_profile_uri={result.IsProfileUri}");
        lines.Add($"reason={result.Reason}");
        WriteSessionCheckLines(lines, logPath);
        return result.IsValid ? 0 : 1;
    }
    catch (Exception ex)
    {
        lines.Add("valid=False");
        lines.Add($"exception_type={ex.GetType().FullName}");
        lines.Add($"exception_message={ex.Message}");
        if (ex.InnerException is not null)
        {
            lines.Add($"inner_exception_type={ex.InnerException.GetType().FullName}");
            lines.Add($"inner_exception_message={ex.InnerException.Message}");
        }

        WriteSessionCheckLines(lines, logPath);
        return 3;
    }
}

static async Task<ArcaSessionCheckResult> CheckSessionWithPlainClientAsync(CookieContainer cookies)
{
    var profileUri = new Uri("https://arca.live/settings/profile");
    using var handler = new HttpClientHandler
    {
        CookieContainer = cookies,
        AutomaticDecompression = DecompressionMethods.All,
        UseCookies = true
    };
    using var client = new HttpClient(handler)
    {
        Timeout = Timeout.InfiniteTimeSpan
    };
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9,en;q=0.8");
    client.DefaultRequestHeaders.Referrer = profileUri;

    using var response = await client.GetAsync(profileUri);
    var html = await response.Content.ReadAsStringAsync();
    var statusCode = (int)response.StatusCode;
    var finalUri = response.RequestMessage?.RequestUri;
    var hasForbiddenMarker = html.Contains("ERROR 403", StringComparison.OrdinalIgnoreCase)
                             || html.Contains("권한이 없습니다.", StringComparison.OrdinalIgnoreCase);
    var isProfileUri = finalUri is not null
                       && finalUri.Host.EndsWith("arca.live", StringComparison.OrdinalIgnoreCase)
                       && finalUri.AbsolutePath.Contains("/settings/profile", StringComparison.OrdinalIgnoreCase);
    var valid = statusCode is not (401 or 403 or 451) && !hasForbiddenMarker && isProfileUri;
    return new ArcaSessionCheckResult(valid, statusCode, finalUri?.ToString(), hasForbiddenMarker, isProfileUri, valid ? "OK" : $"HTTP {statusCode}");
}

static string GetSessionCheckLogPath(string[] args)
{
    var explicitPath = args
        .FirstOrDefault(arg => arg.StartsWith("--check-session-log=", StringComparison.OrdinalIgnoreCase))
        ?.Split('=', 2)[1];
    return string.IsNullOrWhiteSpace(explicitPath)
        ? Path.Combine(AppContext.BaseDirectory, "session-check.log")
        : Path.GetFullPath(explicitPath);
}

static void WriteSessionCheckLines(IReadOnlyList<string> lines, string logPath)
{
    foreach (var line in lines)
    {
        Console.WriteLine(line);
    }

    var dir = Path.GetDirectoryName(logPath);
    if (!string.IsNullOrWhiteSpace(dir))
    {
        Directory.CreateDirectory(dir);
    }

    File.WriteAllLines(logPath, lines, Encoding.UTF8);
    Console.Out.Flush();
}

internal sealed class MainWindow : Window
{
    private readonly List<TextBox> _urlBoxes = [];
    private readonly CookieJar _cookieJar = CookieJar.Default();
    private readonly AppSettingsStore _settingsStore = AppSettingsStore.Default();
    private readonly DownloadService _downloadService = new();
    private readonly AsyncPauseGate _pauseGate = new();
    private readonly ObservableValue<string> _loginStatus = new("미로그인");
    private readonly AccountSessionStatusStore _accountSessionStatus = new();
    private readonly ObservableValue<double> _totalProgress = new(0);
    private readonly ObservableValue<string> _totalProgressText = new("");
    private readonly ObservableValue<string> _totalEtaText = new("");
    private readonly ObservableValue<double> _imageProgress = new(0);
    private readonly ObservableValue<string> _imageProgressText = new("");
    private readonly ObservableValue<string> _imageSpeedText = new("");
    private readonly ObservableValue<string> _imageEtaText = new("");

    private CookieContainer _cookies = new();
    private string _cookieHeader = "";
    private string _outputDirectory = "";
    private CancellationTokenSource? _downloadCts;
    private StackPanel _urlRows = null!;
    private MultiLineTextBox _logTextBox = null!;
    private CheckBox _originalImageCheckBox = null!;
    private CheckBox _cleanupTempCheckBox = null!;
    private Button _settingsButton = null!;
    private Button _startButton = null!;
    private Button _pauseButton = null!;
    private Button _stopButton = null!;

    public MainWindow()
    {
        Title = "아카라이브 다운로더";
        Padding = new Thickness(0);
        StartupLocation = WindowStartupLocation.CenterScreen;
        WindowSize = WindowSize.Resizable(780, 760, 640, 560);
        Content = BuildContent();
        Loaded += async () => await InitializeSessionAsync();
    }

    private Element BuildContent()
    {
        _urlRows = new StackPanel().Vertical().Spacing(6);
        AddUrlRow("");

        return new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Content(
                new StackPanel()
                    .Vertical()
                    .Spacing(16)
                    .Padding(28, 24)
                    .Children(
                        Header(),
                        Section("URL 목록", UrlSection()),
                        Section("다운로드 설정", SettingsSection()),
                        ActionButtons(),
                        ProgressSection(),
                        LogSection()));
    }

    private UIElement Header()
    {
        _settingsButton = new Button()
            .DockRight()
            .Content("설정")
            .OnClick(async () => await ShowSettingsAsync());

        return new DockPanel()
            .Spacing(10)
            .Children(
                _settingsButton,
                new StackPanel()
                    .Horizontal()
                    .Spacing(10)
                    .Children(
                        new Image().Source(ImageSource.FromBytes(ReadEmbeddedResource("arca_icon.png"))).Size(36, 36),
                        new StackPanel()
                            .Vertical()
                            .Children(
                                new TextBlock().Text("아카라이브 다운로더").FontSize(22).Bold(),
                                new TextBlock().Text("게시글 URL을 입력하면 이미지 포함 ZIP으로 저장합니다").FontSize(12))));
    }

    private UIElement Section(string title, UIElement content)
    {
        return new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                new TextBlock().Text(title).Bold(),
                new Border()
                    .Padding(14)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .Child(content));
    }

    private UIElement UrlSection()
    {
        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new DockPanel()
                    .Children(
                        new Button()
                            .DockRight()
                            .Content("URL 추가")
                            .OnClick(() => AddUrlRow("")),
                        new TextBlock().Text("다운로드할 게시글 URL을 입력하세요").CenterVertical()),
                _urlRows);
    }

    private void AddUrlRow(string value)
    {
        var box = new TextBox { Text = value };
        DockPanel row = null!;
        row = new DockPanel()
            .Spacing(8)
            .Children(
                new Button()
                    .DockRight()
                    .Content("삭제")
                    .OnClick(() =>
                    {
                        if (_urlBoxes.Count <= 1)
                        {
                            return;
                        }

                        _urlBoxes.Remove(box);
                        _urlRows.Remove(row);
                    }),
                box);

        _urlBoxes.Add(box);
        _urlRows.Add(row);
    }

    private UIElement SettingsSection()
    {
        _originalImageCheckBox = new CheckBox().Content("이미지 원본 다운로드 (체크 해제 시 미리보기 화질로 다운로드)").IsChecked(true);
        _cleanupTempCheckBox = new CheckBox().Content("완료된 임시 다운로드 삭제 (.arca_tmp)").IsChecked(false);
        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                _originalImageCheckBox,
                _cleanupTempCheckBox);
    }

    private Element ActionButtons()
    {
        _startButton = new Button().Content("다운로드 시작").Height(44).OnClick(async () => await StartAsync());
        _pauseButton = new Button().Content("일시정지").Height(44).Apply(b => b.IsEnabled = false).OnClick(PauseOrResume);
        _stopButton = new Button().Content("중지").Height(44).Apply(b => b.IsEnabled = false).OnClick(StopDownload);

        return new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Children(_startButton, _pauseButton, _stopButton);
    }

    private Element ProgressSection()
    {
        return new Border()
            .Padding(14)
            .BorderThickness(1)
            .CornerRadius(6)
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        ProgressRow("전체", _totalProgress, _totalProgressText, _totalEtaText),
                        ProgressRow("이미지", _imageProgress, _imageProgressText, _imageSpeedText, _imageEtaText)));
    }

    private static Element ProgressRow(string label, ObservableValue<double> value, params ObservableValue<string>[] texts)
    {
        var children = new List<Element>
        {
            new TextBlock().Text(label).Width(50).CenterVertical(),
            new ProgressBar().Minimum(0).Maximum(100).BindValue(value)
        };

        children.AddRange(texts.Select(text => new TextBlock().BindText(text).Width(120).CenterVertical()));
        return new DockPanel().Spacing(8).Children(children.ToArray());
    }

    private Element LogSection()
    {
        _logTextBox = new MultiLineTextBox()
            .Height(220)
            .Wrap(true)
            .FontFamily("Consolas");
        _logTextBox.IsReadOnly = true;
        return _logTextBox;
    }

    private async Task LoadCookiesAsync()
    {
        _cookies = await _cookieJar.LoadAsync();
        var saved = CookieJar.FromContainer(_cookies, new Uri("https://arca.live/"));
        SetLoginStatus(saved.Count > 0 ? "저장된 쿠키" : "미로그인");
        if (saved.Count == 0)
        {
            _accountSessionStatus.Reset();
        }
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        _outputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : settings.OutputDirectory;
        await SaveSettingsAsync();
    }

    private async Task InitializeSessionAsync()
    {
        await LoadSettingsAsync();
        await LoadCookiesAsync();
        if (await HasValidSessionAsync())
        {
            AppendLog("[*] 저장된 아카라이브 세션을 확인했습니다.");
            return;
        }

        SetLoginStatus("로그인 필요");
        AppendLog("[*] 저장된 세션이 없거나 만료되어 로그인 창을 엽니다.");
        await LoginAsync(this);
    }

    private async Task<bool> HasValidSessionAsync()
    {
        var saved = CookieJar.FromContainer(_cookies, new Uri("https://arca.live/"));
        if (saved.Count == 0)
        {
            return false;
        }

        try
        {
            return await ArcaSessionValidator.HasValidSessionAsync(_cookies);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            AppendLog($"[WARN] 세션 검증 실패: {ex.Message}");
            SetLoginStatus("검증 실패");
            return true;
        }
    }

    private async Task ShowSettingsAsync()
    {
        var settingsWindow = new SettingsWindow(
            _cookieHeader,
            _outputDirectory,
            _loginStatus,
            _accountSessionStatus,
            LoginAsync,
            SaveCookieAsync,
            ClearCookieAsync,
            TestSavedSessionAsync,
            async outputDirectory =>
            {
                _outputDirectory = outputDirectory;
                await SaveSettingsAsync();
            });

        await settingsWindow.ShowDialogAsync(this);
        _cookieHeader = settingsWindow.CookieHeader;
        _outputDirectory = settingsWindow.OutputDirectory;
        await SaveSettingsAsync();
    }

    private async Task SaveCookieAsync(string cookieHeader)
    {
        _cookieHeader = cookieHeader;
        await _cookieJar.SaveFromHeaderAsync(_cookieHeader);
        await LoadCookiesAsync();
        AppendLog("[*] 수동 쿠키를 저장했습니다.");
        _ = TestSavedSessionAsync();
    }

    private async Task ClearCookieAsync()
    {
        if (File.Exists(_cookieJar.Path))
        {
            File.Delete(_cookieJar.Path);
        }

        _cookieHeader = "";
        await LoadCookiesAsync();
        _accountSessionStatus.Reset();
        AppendLog("[*] 저장된 쿠키를 삭제했습니다.");
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(new AppSettings(_outputDirectory));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendLog($"[WARN] 설정 저장 실패: {ex.Message}");
        }
    }

    private async Task LoginAsync(Window owner)
    {
        SetLoginStatus("로그인 중...");
        try
        {
            var loginWindow = new LoginWindow();
            await loginWindow.ShowDialogAsync(owner);
            if (loginWindow.Cookies.Count > 0)
            {
                await _cookieJar.SaveAsync(loginWindow.Cookies);
                await LoadCookiesAsync();
                _accountSessionStatus.Succeed();
                AppendLog($"[*] WebView2 로그인 쿠키 {loginWindow.Cookies.Count}개를 저장했습니다.");
            }
            else
            {
                SetLoginStatus("로그인 취소");
                AppendLog("[*] 로그인을 취소했습니다.");
            }
        }
        catch (Exception ex)
        {
            SetLoginStatus("로그인 실패");
            AppendLog($"[ERROR] 로그인 실패: {ex.Message}");
        }
    }

    private async Task TestSavedSessionAsync()
    {
        var cookies = await _cookieJar.LoadAsync();
        var saved = CookieJar.FromContainer(cookies, new Uri("https://arca.live/"));
        if (saved.Count == 0)
        {
            _accountSessionStatus.Fail();
            SetLoginStatus("미로그인");
            AppendLog("[*] 저장된 세션이 없어 접속 테스트에 실패했습니다.");
            return;
        }

        _accountSessionStatus.Checking();

        try
        {
            var result = await ArcaSessionValidator.CheckSessionAsync(cookies);
            AppendLog($"[*] 접속 테스트: valid={result.IsValid}, status={result.StatusCode}, final={result.FinalUri}, reason={result.Reason}");
            if (result.IsValid)
            {
                _accountSessionStatus.Succeed();
                SetLoginStatus("유효함");
                AppendLog("[*] 저장된 아카라이브 세션이 유효합니다.");
            }
            else if (IsInconclusiveHttpClientBlock(result))
            {
                _accountSessionStatus.Reset();
                SetLoginStatus("검증 보류");
                AppendLog("[WARN] .NET 10 HttpClient profile 요청이 403으로 차단되어 세션 유효성을 확정하지 않았습니다.");
            }
            else
            {
                _accountSessionStatus.Fail();
                SetLoginStatus("쿠키 갱신 필요");
                AppendLog("[*] 저장된 아카라이브 세션이 만료되었습니다. 다운로드 시 다시 로그인합니다.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            _accountSessionStatus.Fail();
            SetLoginStatus("검증 실패");
            AppendLog($"[WARN] 세션 검증 실패: {ex.Message}");
        }
    }

    private static bool IsInconclusiveHttpClientBlock(ArcaSessionCheckResult result)
    {
        return result.StatusCode == 403
               && result.IsProfileUri
               && !result.HasForbiddenMarker;
    }

    private async Task StartAsync()
    {
        var urls = _urlBoxes.Select(box => (box.Text ?? "").Trim()).Where(text => text.Length > 0).ToList();
        if (urls.Count == 0)
        {
            await MessageBox.NotifyAsync("URL을 입력하세요.", PromptIconKind.Info, owner: this);
            return;
        }

        _downloadCts = new CancellationTokenSource();
        _pauseGate.Resume();
        SetDownloading(true);
        await SaveSettingsAsync();
        _cookies = await _cookieJar.LoadAsync();

        try
        {
            foreach (var url in urls)
            {
                var request = new DownloadRequest(
                    url,
                    _outputDirectory,
                    _cookieHeader,
                    _originalImageCheckBox.IsChecked == true,
                    _cleanupTempCheckBox.IsChecked == true);

                var result = await _downloadService.DownloadAsync(
                    request,
                    _cookies,
                    _pauseGate,
                    new Progress<string>(AppendLog),
                    new Progress<DownloadProgress>(UpdateProgress),
                    FetchArticleHtmlWithWebViewAsync,
                    _downloadCts.Token);

                AppendLog($"[DONE] {result.ZipPath} ({result.DownloadedImages}/{result.TotalImages})");
                var cookies = CookieJar.FromContainer(_cookies, new Uri("https://arca.live/"));
                await _cookieJar.SaveAsync(cookies, _downloadCts.Token);
            }
        }
        catch (AuthenticationRequiredException ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            SetLoginStatus("쿠키 갱신 필요");
            await MessageBox.NotifyAsync(
                "저장된 쿠키가 유효하지 않습니다. 수동 쿠키를 저장하거나 로그인을 다시 실행하세요.",
                PromptIconKind.Warning,
                owner: this);
        }
        catch (OperationCanceledException)
        {
            AppendLog("[ERROR] 사용자가 다운로드를 중지했습니다.");
        }
        catch (Exception ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
        }
        finally
        {
            SetDownloading(false);
        }
    }

    private async Task<string> FetchArticleHtmlWithWebViewAsync(Uri uri, CancellationToken cancellationToken)
    {
        AppendLog("[*] WebView2 브라우저 세션으로 게시글을 엽니다.");
        var window = new ArticleHtmlWebViewWindow(uri);
        await window.ShowDialogAsync(this);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(window.Html))
        {
            throw new InvalidOperationException(window.ErrorMessage ?? "WebView2에서 게시글 HTML을 가져오지 못했습니다.");
        }

        AppendLog("[*] WebView2에서 게시글 HTML을 가져왔습니다.");
        return window.Html;
    }

    private void PauseOrResume()
    {
        if (_pauseGate.IsPaused)
        {
            _pauseGate.Resume();
            SetButtonText(_pauseButton, "일시정지");
            AppendLog("[*] 다운로드 재개");
        }
        else
        {
            _pauseGate.Pause();
            SetButtonText(_pauseButton, "재개");
            AppendLog("[*] 다운로드 일시정지");
        }
    }

    private void StopDownload()
    {
        if (MessageBox.AskYesNo("다운로드를 중지할까요?", PromptIconKind.Question, owner: this))
        {
            _downloadCts?.Cancel();
            _pauseGate.Resume();
        }
    }

    private void SetDownloading(bool downloading)
    {
        _settingsButton.IsEnabled = !downloading;
        _startButton.IsEnabled = !downloading;
        _pauseButton.IsEnabled = downloading;
        _stopButton.IsEnabled = downloading;
        if (!downloading)
        {
            SetButtonText(_pauseButton, "일시정지");
        }
    }

    private void UpdateProgress(DownloadProgress progress)
    {
        Application.Current.Dispatcher!.BeginInvoke(() =>
        {
            _totalProgress.Value = progress.TotalImages == 0 ? 0 : progress.DoneImages * 100.0 / progress.TotalImages;
            _totalProgressText.Value = progress.TotalImages == 0 ? "" : $"{progress.DoneImages} / {progress.TotalImages}";
            _totalEtaText.Value = string.IsNullOrEmpty(TextHelpers.FormatDuration(progress.TotalEta))
                ? ""
                : $"전체 {TextHelpers.FormatDuration(progress.TotalEta)} 남음";

            _imageProgress.Value = progress.CurrentImageTotalBytes > 0
                ? progress.CurrentImageBytes * 100.0 / progress.CurrentImageTotalBytes
                : 0;
            _imageProgressText.Value = progress.CurrentImageTotalBytes > 0
                ? $"{TextHelpers.FormatBytes(progress.CurrentImageBytes)} / {TextHelpers.FormatBytes(progress.CurrentImageTotalBytes)}"
                : "";
            _imageSpeedText.Value = progress.CurrentImageBytesPerSecond > 0
                ? $"{TextHelpers.FormatBytes((long)progress.CurrentImageBytesPerSecond)}/s"
                : "";
            _imageEtaText.Value = string.IsNullOrEmpty(TextHelpers.FormatDuration(progress.CurrentImageEta))
                ? ""
                : TextHelpers.FormatDuration(progress.CurrentImageEta);
        });
    }

    private void AppendLog(string message)
    {
        Application.Current.Dispatcher!.BeginInvoke(() =>
        {
            _logTextBox.Text = $"{_logTextBox.Text}{message}{Environment.NewLine}";
        });
    }

    private void SetLoginStatus(string status)
    {
        Application.Current.Dispatcher!.BeginInvoke(() =>
        {
            _loginStatus.Value = status;
        });
    }

    private static void SetButtonText(Button button, string text)
    {
        button.Content = new TextBlock { Text = text };
    }

    private static byte[] ReadEmbeddedResource(string name)
    {
        var assembly = typeof(Program).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded resource not found: {name}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

internal sealed class AccountSessionStatusStore
{
    public ObservableValue<bool> ShowSuccess { get; } = new(false);

    public ObservableValue<bool> ShowFailure { get; } = new(false);

    public ObservableValue<bool> ShowBusy { get; } = new(false);

    public ObservableValue<string> BusyText { get; } = new("");

    public void Reset()
    {
        Update(() =>
        {
            ShowSuccess.Value = false;
            ShowFailure.Value = false;
            ShowBusy.Value = false;
            BusyText.Value = "";
        });
    }

    public void Checking()
    {
        Update(() =>
        {
            ShowSuccess.Value = false;
            ShowFailure.Value = false;
            ShowBusy.Value = true;
            BusyText.Value = "접속 테스트 중...";
        });
    }

    public void Succeed()
    {
        Update(() =>
        {
            ShowSuccess.Value = true;
            ShowFailure.Value = false;
            ShowBusy.Value = false;
            BusyText.Value = "";
        });
    }

    public void Fail()
    {
        Update(() =>
        {
            ShowSuccess.Value = false;
            ShowFailure.Value = true;
            ShowBusy.Value = false;
            BusyText.Value = "";
        });
    }

    private static void Update(Action action)
    {
        Application.Current.Dispatcher!.BeginInvoke(action);
    }
}

internal sealed class ArticleHtmlWebViewWindow : Window
{
    private readonly Uri _uri;
    private readonly ObservableValue<string> _status = new("게시글을 여는 중...");
    private readonly Aprillz.MewUI.Controls.WebView2 _browser = new()
    {
        UseSharedEnvironment = false,
        UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArcaDownloader",
            "WebView2")
    };

    private bool _captureInProgress;

    public ArticleHtmlWebViewWindow(Uri uri)
    {
        _uri = uri;
        Title = "WebView2 게시글 가져오기";
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.Resizable(960, 720, 720, 520);

        Content = new DockPanel()
            .Spacing(8)
            .Padding(12)
            .Children(
                new DockPanel()
                    .DockTop()
                    .Spacing(8)
                    .Children(
                        new Button().Content("다시 시도").OnClick(() => Navigate()),
                        new Button().Content("취소").DockRight().OnClick(Close),
                        new TextBlock().BindText(_status).CenterVertical()),
                _browser);

        _browser.CoreWebView2InitializationCompleted += _ => Navigate();
        _browser.NavigationCompleted += async e =>
        {
            if (!e.IsSuccess || e.HttpStatusCode is < 200 or >= 400)
            {
                ErrorMessage = $"WebView2 게시글 요청 실패: HTTP {e.HttpStatusCode}";
                _status.Value = ErrorMessage;
                return;
            }

            await CaptureHtmlAsync();
        };
    }

    public string? Html { get; private set; }

    public string? ErrorMessage { get; private set; }

    private void Navigate()
    {
        Html = null;
        ErrorMessage = null;
        _status.Value = "게시글을 여는 중...";
        _browser.Source = _uri;
    }

    private async Task CaptureHtmlAsync()
    {
        if (_captureInProgress)
        {
            return;
        }

        _captureInProgress = true;
        try
        {
            _status.Value = "게시글 HTML을 읽는 중...";
            await Task.Delay(500);
            var result = await _browser.ExecuteScriptAsync("document.documentElement.outerHTML");
            Html = string.IsNullOrWhiteSpace(result)
                ? ""
                : JsonSerializer.Deserialize(result, MewUiJsonContext.Default.String) ?? "";
            if (string.IsNullOrWhiteSpace(Html))
            {
                ErrorMessage = "WebView2 스크립트 결과가 비어 있습니다.";
                _status.Value = ErrorMessage;
                return;
            }

            _status.Value = "게시글 HTML을 가져왔습니다.";
            Close();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"WebView2 HTML 읽기 실패: {ex.Message}";
            _status.Value = ErrorMessage;
        }
        finally
        {
            _captureInProgress = false;
        }
    }
}

internal sealed class SettingsWindow : Window
{
    private readonly TextBox _cookieTextBox;
    private readonly TextBox _outputDirectoryTextBox;

    public SettingsWindow(
        string cookieHeader,
        string outputDirectory,
        ObservableValue<string> loginStatus,
        AccountSessionStatusStore accountSessionStatus,
        Func<Window, Task> loginAsync,
        Func<string, Task> saveCookieAsync,
        Func<Task> clearCookieAsync,
        Func<Task> testSessionAsync,
        Func<string, Task> saveOutputDirectoryAsync)
    {
        Title = "설정";
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.Fixed(620, 430);

        _cookieTextBox = new TextBox
        {
            Text = cookieHeader
        }.Placeholder("수동 쿠키 문자열: key=value; key2=value2");
        _outputDirectoryTextBox = new TextBox
        {
            Text = outputDirectory
        };

        Content = new StackPanel()
            .Vertical()
            .Spacing(16)
            .Padding(24)
            .Children(
                Section("저장 위치", OutputSection(saveOutputDirectoryAsync)),
                Section("계정 관리", AccountSection(loginStatus, accountSessionStatus, loginAsync, saveCookieAsync, clearCookieAsync, testSessionAsync)),
                new DockPanel()
                    .Children(
                        new Button()
                            .DockRight()
                            .Content("닫기")
                            .OnClick(Close)));
    }

    public string CookieHeader => _cookieTextBox.Text ?? "";

    public string OutputDirectory => _outputDirectoryTextBox.Text ?? "";

    private UIElement OutputSection(Func<string, Task> saveOutputDirectoryAsync)
    {
        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new DockPanel()
                    .Spacing(8)
                    .Children(
                        new Button()
                            .DockRight()
                            .Content("폴더 선택")
                            .OnClick(async () =>
                            {
                                var folder = FileDialog.SelectFolder(new FolderDialogOptions { Owner = Handle });
                                if (!string.IsNullOrWhiteSpace(folder))
                                {
                                    _outputDirectoryTextBox.Text = folder;
                                    await saveOutputDirectoryAsync(folder);
                                }
                            }),
                        _outputDirectoryTextBox),
                new TextBlock()
                    .FontSize(11)
                    .Text("직접 입력하거나 폴더를 선택하면 다음 다운로드부터 적용됩니다."));
    }

    private UIElement AccountSection(
        ObservableValue<string> loginStatus,
        AccountSessionStatusStore accountSessionStatus,
        Func<Window, Task> loginAsync,
        Func<string, Task> saveCookieAsync,
        Func<Task> clearCookieAsync,
        Func<Task> testSessionAsync)
    {
        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new DockPanel()
                    .Spacing(8)
                    .Children(
                        new Button().Content("로그인").OnClick(async () => await loginAsync(this)),
                        new Button().Content("접속 테스트").OnClick(async () => await testSessionAsync()),
                        new Button().Content("쿠키 저장").OnClick(async () => await saveCookieAsync(CookieHeader)),
                        new Button().Content("삭제").OnClick(async () =>
                        {
                            await clearCookieAsync();
                            _cookieTextBox.Text = "";
                        }),
                        new TextBlock().BindText(loginStatus).DockLeft().Width(120).CenterVertical().Bold()),
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("● 로그인 성공")
                            .Foreground(new Color(34, 197, 94))
                            .Bold()
                            .BindIsVisible(accountSessionStatus.ShowSuccess),
                        new TextBlock()
                            .Text("● 로그인 실패")
                            .Foreground(new Color(239, 68, 68))
                            .Bold()
                            .BindIsVisible(accountSessionStatus.ShowFailure),
                        new TextBlock()
                            .BindText(accountSessionStatus.BusyText)
                            .CenterVertical()
                            .BindIsVisible(accountSessionStatus.ShowBusy)),
                _cookieTextBox,
                new TextBlock()
                    .FontSize(11)
                    .Text("저장된 쿠키가 만료되면 다운로드 시 자동으로 로그인 창이 열립니다. 이 화면은 계정 전환, 삭제, 수동 쿠키 저장이 필요할 때만 사용하세요."));
    }

    private static UIElement Section(string title, UIElement content)
    {
        return new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                new TextBlock().Text(title).Bold(),
                new Border()
                    .Padding(14)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .Child(content));
    }
}

internal sealed class LoginWindow : Window
{
    private static readonly Uri ArcaLiveUri = new("https://arca.live/");
    private static readonly Uri LoginUri = new("https://arca.live/u/login");
    private static readonly Uri ProfileUri = new("https://arca.live/settings/profile");

    private readonly Aprillz.MewUI.Controls.WebView2 _browser = new()
    {
        UseSharedEnvironment = false,
        UserDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ArcaDownloader",
            "WebView2")
    };
    private readonly DispatcherTimer _cookiePollTimer = new(TimeSpan.FromSeconds(1));
    private bool _completionInProgress;
    private int _lastNavigationStatusCode;
    private bool _lastNavigationSucceeded;

    public IReadOnlyList<PersistedCookie> Cookies { get; private set; } = [];

    public LoginWindow()
    {
        Title = "아카라이브 로그인";
        StartupLocation = WindowStartupLocation.CenterOwner;
        WindowSize = WindowSize.Resizable(960, 720, 720, 520);

        Content = new DockPanel()
            .Spacing(8)
            .Padding(12)
            .Children(
                new DockPanel()
                    .DockTop()
                    .Spacing(8)
                    .Children(
                        new Button().Content("새로고침").OnClick(() => _browser.Reload()),
                        new Button().Content("로그인 완료").DockRight().OnClick(async () => await TryCompleteAsync(force: true)),
                        new TextBlock().Text("로그인 세션이 확인되면 자동으로 완료됩니다.").CenterVertical()),
                _browser);

        _cookiePollTimer.Tick += async () => await TryCompleteAsync(force: false);
        _browser.CoreWebView2InitializationCompleted += _ =>
        {
            _browser.Source = LoginUri;
            _cookiePollTimer.Start();
        };
        _browser.SourceChanged += async _ => await TryCompleteAsync(force: false);
        _browser.NavigationCompleted += async e =>
        {
            _lastNavigationStatusCode = e.HttpStatusCode;
            _lastNavigationSucceeded = e.IsSuccess;
            await TryCompleteAsync(force: false);
        };
        Closed += () =>
        {
            _cookiePollTimer.Stop();
            _cookiePollTimer.Dispose();
        };
    }

    private async Task TryCompleteAsync(bool force)
    {
        if (_completionInProgress)
        {
            return;
        }

        _completionInProgress = true;
        try
        {
            if (_browser.CoreWebView2 is null)
            {
                return;
            }

            if (_browser.Source is null || !IsArcaLiveUri(_browser.Source))
            {
                _browser.Source = ArcaLiveUri;
                return;
            }

            if (!IsProfileUri(_browser.Source))
            {
                if (force || IsProbablyPostLoginPage(_browser.Source))
                {
                    _browser.Source = ProfileUri;
                }
                return;
            }

            if (!IsSuccessfulProfileNavigation())
            {
                if (force)
                {
                    await MessageBox.NotifyAsync("프로필 설정 페이지 접근이 거부되었습니다. 로그인 후 다시 눌러주세요.", PromptIconKind.Warning, owner: this);
                }
                return;
            }

            var cookies = await ReadWebViewCookiesAsync();
            if (cookies.Count == 0)
            {
                if (force)
                {
                    await MessageBox.NotifyAsync("WebView2 CookieManager에서 arca.live 쿠키를 찾지 못했습니다.", PromptIconKind.Warning, owner: this);
                }
                return;
            }

            Cookies = cookies;
            Close();
        }
        finally
        {
            _completionInProgress = false;
        }
    }

    private async Task<IReadOnlyList<PersistedCookie>> ReadWebViewCookiesAsync()
    {
        if (GetCoreWebView2ComObject() is not ICoreWebView2_2 core)
        {
            return [];
        }

        core.get_CookieManager(out var manager);
        if (manager is null)
        {
            return [];
        }

        var completion = new TaskCompletionSource<ICoreWebView2CookieList?>();
        var handler = new CoreWebView2GetCookiesCompletedHandler((errorCode, result) =>
        {
            completion.TrySetResult(errorCode.IsSuccess ? result : null);
        });

        manager.GetCookies(PWSTR.From("https://arca.live/"), handler);
        var cookieList = await completion.Task;
        if (cookieList is null)
        {
            return [];
        }

        uint count = 0;
        cookieList.get_Count(ref count);
        var cookies = new List<PersistedCookie>();
        for (uint i = 0; i < count; i++)
        {
            cookieList.GetValueAtIndex(i, out var cookie);
            if (cookie is null)
            {
                continue;
            }

            var name = GetCookieString((out PWSTR value) => cookie.get_Name(out value));
            var value = GetCookieString((out PWSTR value) => cookie.get_Value(out value));
            var domain = GetCookieString((out PWSTR value) => cookie.get_Domain(out value));
            var path = GetCookieString((out PWSTR value) => cookie.get_Path(out value));
            if (string.IsNullOrWhiteSpace(name) || !domain.Contains("arca.live", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            BOOL isSession = default;
            BOOL isSecure = default;
            BOOL isHttpOnly = default;
            double expires = 0;
            cookie.get_IsSession(ref isSession);
            cookie.get_IsSecure(ref isSecure);
            cookie.get_IsHttpOnly(ref isHttpOnly);
            cookie.get_Expires(ref expires);

            cookies.Add(new PersistedCookie(
                name,
                value,
                string.IsNullOrWhiteSpace(domain) ? "arca.live" : domain,
                string.IsNullOrWhiteSpace(path) ? "/" : path,
                isSession ? null : DateTimeOffset.FromUnixTimeSeconds((long)expires),
                isSecure,
                isHttpOnly));
        }

        return cookies;
    }

    private static bool IsArcaLiveUri(Uri uri)
    {
        return uri.Host.Equals("arca.live", StringComparison.OrdinalIgnoreCase)
               || uri.Host.EndsWith(".arca.live", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProfileUri(Uri uri)
    {
        return IsArcaLiveUri(uri)
               && uri.AbsolutePath.Contains("/settings/profile", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsProbablyPostLoginPage(Uri uri)
    {
        return IsArcaLiveUri(uri)
               && !uri.AbsolutePath.Contains("/u/login", StringComparison.OrdinalIgnoreCase)
               && !uri.AbsolutePath.Contains("/settings/profile", StringComparison.OrdinalIgnoreCase);
    }

    private bool IsSuccessfulProfileNavigation()
    {
        return _lastNavigationSucceeded
               && _lastNavigationStatusCode is >= 200 and < 400;
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicProperties, typeof(Aprillz.MewUI.Controls.WebView2))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.NonPublicProperties, typeof(Aprillz.MewUI.Controls.CoreWebView2))]
    private object? GetCoreWebView2ComObject()
    {
        var browserCore = _browser
            .GetType()
            .GetProperty("CoreWebView2Internal", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(_browser);
        if (browserCore is not null)
        {
            return browserCore;
        }

        return _browser.CoreWebView2?
            .GetType()
            .GetProperty("ComObject", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(_browser.CoreWebView2);
    }

    private delegate HRESULT CookieStringGetter(out PWSTR value);

    private static string GetCookieString(CookieStringGetter getter)
    {
        PWSTR value = default;
        getter(out value);
        return value.ToStringAndDispose() ?? "";
    }
}

internal sealed class AppSettingsStore
{
    private const string Section = "[General]";
    private const string OutputDirectoryKey = "OutputDirectory";

    private AppSettingsStore(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static AppSettingsStore Default()
    {
        return new AppSettingsStore(System.IO.Path.Combine(AppContext.BaseDirectory, "settings.ini"));
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(Path))
        {
            return new AppSettings(null);
        }

        var lines = await File.ReadAllLinesAsync(Path, Encoding.UTF8, cancellationToken);
        var inGeneral = false;
        string? outputDirectory = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inGeneral = line.Equals(Section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inGeneral)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (key.Equals(OutputDirectoryKey, StringComparison.OrdinalIgnoreCase))
            {
                outputDirectory = value;
            }
        }

        return new AppSettings(string.IsNullOrWhiteSpace(outputDirectory) ? null : outputDirectory);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var outputDirectory = (settings.OutputDirectory ?? "").ReplaceLineEndings("").Trim();
        var content = $"{Section}{Environment.NewLine}{OutputDirectoryKey}={outputDirectory}{Environment.NewLine}";
        await File.WriteAllTextAsync(Path, content, Encoding.UTF8, cancellationToken);
    }
}

internal sealed record AppSettings(string? OutputDirectory);

internal static partial class NativeMethods
{
    public const uint AttachParentProcess = 0xFFFFFFFF;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachConsole(uint processId);
}

internal static class FluentHelpers
{
    public static T Apply<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
