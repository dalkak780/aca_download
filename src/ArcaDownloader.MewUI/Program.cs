using System.Net;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
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

internal sealed class MainWindow : Window
{
    private readonly List<TextBox> _urlBoxes = [];
    private readonly CookieJar _cookieJar = CookieJar.Default();
    private readonly AppSettingsStore _settingsStore = AppSettingsStore.Default();
    private readonly DownloadService _downloadService = new();
    private readonly AsyncPauseGate _pauseGate = new();
    private readonly ObservableValue<string> _loginStatus = new("미로그인");
    private readonly ObservableValue<double> _totalProgress = new(0);
    private readonly ObservableValue<string> _totalProgressText = new("");
    private readonly ObservableValue<string> _totalEtaText = new("");
    private readonly ObservableValue<double> _imageProgress = new(0);
    private readonly ObservableValue<string> _imageProgressText = new("");
    private readonly ObservableValue<string> _imageSpeedText = new("");
    private readonly ObservableValue<string> _imageEtaText = new("");

    private CookieContainer _cookies = new();
    private CancellationTokenSource? _downloadCts;
    private StackPanel _urlRows = null!;
    private TextBox _cookieTextBox = null!;
    private TextBox _outputDirectoryTextBox = null!;
    private MultiLineTextBox _logTextBox = null!;
    private CheckBox _originalImageCheckBox = null!;
    private Button _startButton = null!;
    private Button _pauseButton = null!;
    private Button _stopButton = null!;

    public MainWindow()
    {
        Title = "아카라이브 다운로더";
        Padding = new Thickness(0);
        StartupLocation = WindowStartupLocation.CenterScreen;
        WindowSize = WindowSize.Resizable(780, 850, 640, 600);
        Content = BuildContent();
        Loaded += async () => await InitializeSessionAsync();
    }

    private Element BuildContent()
    {
        _outputDirectoryTextBox = new TextBox();

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
                        Section("아카라이브 로그인 (선택 - HTTP 451 차단 우회)", LoginSection()),
                        Section("저장 위치", OutputSection()),
                        Section("다운로드 설정", SettingsSection()),
                        ActionButtons(),
                        ProgressSection(),
                        LogSection()));
    }

    private UIElement Header()
    {
        return new StackPanel()
            .Horizontal()
            .Spacing(10)
            .Children(
                new Image().Source(ImageSource.FromBytes(ReadEmbeddedResource("arca_icon.png"))).Size(36, 36),
                new StackPanel()
                    .Vertical()
                    .Children(
                        new TextBlock().Text("아카라이브 다운로더").FontSize(22).Bold(),
                        new TextBlock().Text("게시글 URL을 입력하면 이미지 포함 ZIP으로 저장합니다").FontSize(12)));
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

    private UIElement LoginSection()
    {
        _cookieTextBox = new TextBox().Placeholder("수동 쿠키 문자열: key=value; key2=value2");

        return new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new DockPanel()
                    .Spacing(8)
                    .Children(
                        new Button().Content("아카라이브 로그인").OnClick(async () => await LoginAsync()),
                        new Button().Content("쿠키 저장").OnClick(async () => await SaveCookieAsync()),
                        new Button().Content("삭제").OnClick(async () => await ClearCookieAsync()),
                        new TextBlock().BindText(_loginStatus).DockLeft().Width(150).CenterVertical().Bold()),
                _cookieTextBox,
                new TextBlock()
                    .FontSize(11)
                    .Text("쿠키는 LocalAppData\\ArcaDownloader\\cookies.json에 저장되며, invalid할 때만 다시 로그인하면 됩니다."));
    }

    private UIElement OutputSection()
    {
        return new DockPanel()
            .Spacing(8)
            .Children(
                new Button()
                    .DockRight()
                    .Content("폴더 선택")
                    .OnClick(() =>
                    {
                        var folder = FileDialog.SelectFolder(new FolderDialogOptions { Owner = Handle });
                        if (!string.IsNullOrWhiteSpace(folder))
                        {
                            _outputDirectoryTextBox.Text = folder;
                            _ = SaveSettingsAsync();
                        }
                    }),
                _outputDirectoryTextBox);
    }

    private UIElement SettingsSection()
    {
        _originalImageCheckBox = new CheckBox().Content("이미지 원본 다운로드 (체크 해제 시 미리보기 화질로 다운로드)").IsChecked(true);
        return _originalImageCheckBox;
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
        _loginStatus.Value = saved.Count > 0 ? "저장된 쿠키" : "미로그인";
    }

    private async Task LoadSettingsAsync()
    {
        var settings = await _settingsStore.LoadAsync();
        _outputDirectoryTextBox.Text = string.IsNullOrWhiteSpace(settings.OutputDirectory)
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

        _loginStatus.Value = "로그인 필요";
        AppendLog("[*] 저장된 세션이 없거나 만료되어 로그인 창을 엽니다.");
        await LoginAsync();
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
            _loginStatus.Value = "검증 실패";
            return true;
        }
    }

    private async Task SaveCookieAsync()
    {
        await _cookieJar.SaveFromHeaderAsync(_cookieTextBox.Text ?? "");
        await LoadCookiesAsync();
        AppendLog("[*] 수동 쿠키를 저장했습니다.");
    }

    private async Task ClearCookieAsync()
    {
        if (File.Exists(_cookieJar.Path))
        {
            File.Delete(_cookieJar.Path);
        }

        _cookieTextBox.Text = "";
        await LoadCookiesAsync();
        AppendLog("[*] 저장된 쿠키를 삭제했습니다.");
    }

    private async Task SaveSettingsAsync()
    {
        try
        {
            await _settingsStore.SaveAsync(new AppSettings(_outputDirectoryTextBox.Text));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppendLog($"[WARN] 설정 저장 실패: {ex.Message}");
        }
    }

    private async Task LoginAsync()
    {
        _loginStatus.Value = "로그인 중...";
        try
        {
            var loginWindow = new LoginWindow();
            await loginWindow.ShowDialogAsync(this);
            if (loginWindow.Cookies.Count > 0)
            {
                await _cookieJar.SaveAsync(loginWindow.Cookies);
                await LoadCookiesAsync();
                AppendLog($"[*] WebView2 로그인 쿠키 {loginWindow.Cookies.Count}개를 저장했습니다.");
            }
            else
            {
                _loginStatus.Value = "로그인 취소";
                AppendLog("[*] 로그인을 취소했습니다.");
            }
        }
        catch (Exception ex)
        {
            _loginStatus.Value = "로그인 실패";
            AppendLog($"[ERROR] 로그인 실패: {ex.Message}");
        }
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
                    _outputDirectoryTextBox.Text ?? "",
                    _cookieTextBox.Text ?? "",
                    _originalImageCheckBox.IsChecked == true);

                var result = await _downloadService.DownloadAsync(
                    request,
                    _cookies,
                    _pauseGate,
                    new Progress<string>(AppendLog),
                    new Progress<DownloadProgress>(UpdateProgress),
                    _downloadCts.Token);

                AppendLog($"[DONE] {result.ZipPath} ({result.DownloadedImages}/{result.TotalImages})");
                var cookies = CookieJar.FromContainer(_cookies, new Uri("https://arca.live/"));
                await _cookieJar.SaveAsync(cookies, _downloadCts.Token);
            }
        }
        catch (AuthenticationRequiredException ex)
        {
            AppendLog($"[ERROR] {ex.Message}");
            _loginStatus.Value = "쿠키 갱신 필요";
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

internal static class FluentHelpers
{
    public static T Apply<T>(this T value, Action<T> configure)
    {
        configure(value);
        return value;
    }
}
