using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Resources;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Web.WebView2.Core;
using AlekrythaeCore.Media;
using Forms = System.Windows.Forms;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;

namespace AlekrythaeCore
{
    public class CosmicGate : Window
    {
        private WebView2 _webView;
        private bool _hostExitRequested;
        private volatile bool _hostExitClosed;
        private readonly EdgeChatGptDock _edgeChatGptDock;
        private readonly PortableGameStore _gameStore;
        private readonly DataTransferService _dataTransferService;
        private readonly DeveloperBridge _developerBridge;
        private string _jsPath;
        private string _memoryPath; // .alek dosyasının yanındaki .json hafıza dosyası
        private string _rootFolder; // Güvenlik sandbox'ı: .alek dosyasının bulunduğu klasör
        private string _engineDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "KozmikData");
        private bool _startFullscreen;
        private Rect _restoreBounds;
        private bool _isWindowedFullscreen;
        private bool _isTitleBarAreaInWorkingSpace = true;
        private const double TitleBarHeight = 0; // Yerleşik WPF üst şeridi tamamen devre dışı.
        private readonly string _webLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alekrythae_webview.log");
        private CoreWebView2DevToolsProtocolEventReceiver? _consoleReceiver;
        private CoreWebView2DevToolsProtocolEventReceiver? _exceptionReceiver;
        private bool _webViewSuspended;
        private bool _powerStateChangeInProgress;
        private bool _powerStateChangePending;
        private Rect _safeAiCssBounds;
        private double _safeAiCssBoundsZoomFactor = 1.0;
        private bool _hasSafeAiCssBounds;
        private bool _webViewZoomHooked;
        private int _safeAiZoomSyncGeneration;
        private bool _altSSystemCharacterHooked;
        private const int WmSysChar = 0x0106;

        // Desteklenen dosya uzantıları
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".webp", ".gif" };
        private static readonly string[] AudioExtensions = { ".mp3", ".m4a", ".wav", ".ogg" };

        public CosmicGate(string jsPath)
        {
            _jsPath = jsPath;
            // HAFIZA: .alek dosyasının yanına aynı isimle .json yaz
            _memoryPath = jsPath + ".json";
            // GÜVENLİK: Root klasör — JS sadece bu klasör ve alt klasörlerinde işlem yapabilir
            string dir = Path.GetDirectoryName(Path.GetFullPath(jsPath)) ?? AppDomain.CurrentDomain.BaseDirectory;
            _rootFolder = dir.EndsWith(Path.DirectorySeparatorChar.ToString()) ? dir : dir + Path.DirectorySeparatorChar;
            _gameStore = new PortableGameStore(_rootFolder);
            _dataTransferService = new DataTransferService(_rootFolder, this);
            _developerBridge = new DeveloperBridge();
            _startFullscreen = ReadAlekWindowCommand(jsPath);

            try
            {
                string taxonomyAssetDirectory = Path.Combine(_rootFolder, "Assets", "taxonomy");
                TaxonomyEnergyIndexWriter.Write(
                    taxonomyAssetDirectory,
                    frameFileName: "taxonomy_frame.png",
                    maskFileName: "taxonomy_energy_channel.png");
            }
            catch
            {
                // Taksonomi enerji indexi üretilemezse arayüz fallback ile devam eder.
            }

            // Şase Ayarları
            this.WindowStyle = WindowStyle.None;
            this.AllowsTransparency = false;
            this.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 5, 8));
            this.ResizeMode = ResizeMode.NoResize;

            // Uygulama basıldığı anda zınk diye ekrana gelsin diye (Program.cs desteğiyle beraber):
            this.WindowState = WindowState.Normal;
            this.Activate();

            // 1. PENCERE ŞASESİ: Modern, Kenarlıksız ve Transparan
            this.Title = "Ałek’ryŧhæ Core v0.1.2 - " + Path.GetFileName(jsPath);
            this.Width = 1024;
            this.Height = 768;
            this.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            // 2. ANA LAYOUT (Grid)
            // Tek katmanlı düzen: Web çalışma alanı tüm pencereyi kaplar,
            // şeffaf title bar onun üstünde yüzer.
            Grid mainGrid = new Grid
            {
                Background = System.Windows.Media.Brushes.Transparent
            };

            // 3. YERLEŞİK ÜST ŞERİT
            // Yüksekliği sıfırdır ve giriş yakalamaz. Üst alan tamamen WebView'e aittir.
            Border titleBar = new Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Height = TitleBarHeight,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                IsHitTestVisible = false,
                Focusable = false
            };
            System.Windows.Controls.Panel.SetZIndex(titleBar, 100);

            Grid titleContent = new Grid();
            titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            titleBar.Child = titleContent;

            // ==========================================
            // İŞTE BURASI DÜZELDİ: KALİGRAFİ (Sol Üst)
            // ==========================================
            try
            {
                System.Windows.Controls.Image logo = new System.Windows.Controls.Image
                {
                    // Artık dosya aramak yok, direkt uygulamanın kalbinden (pack://) çekiyoruz
                    Source = new BitmapImage(new Uri("pack://application:,,,/Resources/Alekrythae.png", UriKind.RelativeOrAbsolute)),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Margin = new Thickness(15, 0, 0, 0),
                    Height = 22,
                    Opacity = 0.88,
                    Stretch = Stretch.Uniform
                };
                Grid.SetColumn(logo, 0);
                titleContent.Children.Add(logo);
            }
            catch { /* Hata olsa da programı patlatma */ }
            // ==========================================

            // Yerleşik üst şerit devre dışıdır. Pencere komutları F11/F2 üzerinden yönetilir.

            // F11: çalışma alanı tam ekran
            // Shift+F11: title bar alanını Working Space'e dahil et / çıkar
            this.PreviewKeyDown += OnWindowPreviewKeyDown;
            this.PreviewKeyUp += OnWindowPreviewKeyUp;
            ComponentDispatcher.ThreadPreprocessMessage += OnThreadPreprocessMessage;
            _altSSystemCharacterHooked = true;
            this.StateChanged += async (_, _) => await UpdateWebViewPowerStateAsync();
            this.IsVisibleChanged += async (_, _) => await UpdateWebViewPowerStateAsync();
            this.Activated += async (_, _) => await UpdateWebViewPowerStateAsync();
            this.Deactivated += async (_, _) => await UpdateWebViewPowerStateAsync();

            // Yerleşik sağ üst kapatma düğmesi kaldırıldı. Çıkış, F2 ana dairesindeki
            // “Boyuttan Çık” komutundan veya Windows görev çubuğundan yapılır.

            // 4. WEB İÇERİK ALANI
            // Yerleşik WPF üst şerit artık 0 px olduğu için WPF elemanlarını WebView'in
            // üstüne bindirmemiz gerekmiyor. WebView2CompositionControl, Chromium
            // yüzeyini GraphicsCaptureSession ile WPF Image'a taşıdığı için boşta bile
            // gereksiz GPU kopyalama/composition yükü oluşturabiliyor. Standart WebView2
            // doğrudan HWND host yolunu kullanır ve bu uygulamada airspace gereksinimi yoktur.
            _webView = new WebView2
            {
                Visibility = Visibility.Visible,
                DefaultBackgroundColor = System.Drawing.Color.FromArgb(255, 5, 5, 8),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                VerticalAlignment = System.Windows.VerticalAlignment.Stretch
            };

            // AI görünümü WebView2 ile giriş yapmaya çalışmaz. Gerçek Microsoft
            // Edge, chatgpt.com'u uygulama modunda açar ve yalnız pencere düzeyinde
            // Meggy'nin AI alanına kenetlenir. Core sayfa içeriğine, DOM'a, çerezlere,
            // şifrelere veya oturum anahtarlarına erişmez.
            _edgeChatGptDock = new EdgeChatGptDock(this);

            mainGrid.Children.Add(_webView);
            mainGrid.Children.Add(titleBar);
            ApplyTitleBarWorkingSpaceMode();
            this.Content = mainGrid;
            this.SourceInitialized += (s, e) => { if (_startFullscreen) ApplyWindowedFullscreen(); };

            this.Loaded += async (s, e) => {
                try
                {
                    var environmentOptions = new CoreWebView2EnvironmentOptions
                    {
                        AdditionalBrowserArguments = GraphicsBridge.GetAdditionalBrowserArguments()
                    };
                    var env = await CoreWebView2Environment.CreateAsync(null, _engineDataFolder, environmentOptions);
                    await _webView.EnsureCoreWebView2Async(env);
                    _developerBridge.Attach(_webView.CoreWebView2, Dispatcher);
                    if (!_webViewZoomHooked)
                    {
                        _webView.ZoomFactorChanged += OnWebViewZoomFactorChanged;
                        _webViewZoomHooked = true;
                    }
                    // Uygulama kendi F1/F11 ve benzeri kısayollarını yönetir.
                    // Chromium'un yerleşik yardım/accelerator davranışlarının tuşu yutmasını engelle.
                    try { _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false; } catch { }

                    // ====== VIRTUAL HOST MAPPING ======
                    string? alekFolder = Path.GetDirectoryName(Path.GetFullPath(_jsPath));
                    if (!string.IsNullOrEmpty(alekFolder))
                    {
                        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                            "alek-assets.local", alekFolder,
                            CoreWebView2HostResourceAccessKind.Allow);
                    }

                    // Resources klasörü için ayrı virtual host (Bluemoon.png vs.)
                    string resourcesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources");
                    if (Directory.Exists(resourcesFolder))
                    {
                        _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                            "alek-engine.local", resourcesFolder,
                            CoreWebView2HostResourceAccessKind.Allow);
                    }
                    // ==================================

                    // ====== TAŞINABİLİR MEDYA İÇE AKTARIMI ======
                    // Mutlak dış yol yalnız seçilen dosyayı aktif Adventure'ın Media
                    // klasörüne kopyalayacak geçici akış süresince onaylanır.
                    ExternalMediaBridge.RegisterStreaming(_webView.CoreWebView2, _memoryPath);
                    // ==========================================

                    // ====== JS <-> C# KÖPRÜSÜ KURULUMU ======
                    _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                    _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                    _webView.CoreWebView2.ProcessFailed += OnProcessFailed;
#if DEBUG
                    await EnableWebDiagnosticsAsync();
#endif
                    // ========================================

                    FireUpJs();
                }
                catch (Exception ex)
                {
                    AppendWebLog("startup failed: " + ex);
                    System.Windows.MessageBox.Show($"Çekirdek Hatası: {ex.Message}");
                }
            };
        }

        private async Task UpdateWebViewPowerStateAsync()
        {
            if (_webView?.CoreWebView2 == null) return;
            if (_powerStateChangeInProgress)
            {
                _powerStateChangePending = true;
                return;
            }

            _powerStateChangeInProgress = true;
            try
            {
                do
                {
                    _powerStateChangePending = false;

                    bool isHiddenOrMinimized = !IsVisible || WindowState == WindowState.Minimized;
                    bool isInactive = !IsActive;

                    if (isHiddenOrMinimized)
                    {
                        // WebView2, TrySuspendAsync çağrılırken görünür olmamalıdır.
                        // WPF Visibility değişikliği controller'ın IsVisible durumunu da
                        // kapatır. Aksi halde TrySuspendAsync ERROR_INVALID_STATE ile
                        // başarısız olur ve renderer/GPU süreci çalışmaya devam eder.
                        if (_webView.Visibility != Visibility.Hidden)
                            _webView.Visibility = Visibility.Hidden;

                        // TrySuspendAsync ve MemoryUsageTargetLevel iki ayrı güç
                        // yönetimi yoludur. Pencere minimize olmadan hemen önce odaksız
                        // durumda Low uygulanmış olabilir; suspend yoluna girmeden önce
                        // hedefi normale döndür.
                        try
                        {
                            _webView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
                        }
                        catch { }

                        // DependencyProperty değişikliğinin WebView2 controller'a
                        // aktarılmasına bir UI turu ver. Suspend yine de best-effort'tur.
                        await Task.Yield();

                        if (!_webViewSuspended)
                        {
                            bool suspended = false;
                            try
                            {
                                suspended = await _webView.CoreWebView2.TrySuspendAsync();
                            }
                            catch (Exception ex)
                            {
                                AppendWebLog("suspend: " + ex.Message);
                            }

                            _webViewSuspended = suspended;
                        }
                    }
                    else
                    {
                        if (_webViewSuspended)
                        {
                            _webView.CoreWebView2.Resume();
                            _webViewSuspended = false;
                        }

                        if (_webView.Visibility != Visibility.Visible)
                            _webView.Visibility = Visibility.Visible;

                        // Pencere başka bir uygulamanın arkasında ama hâlâ ekranda ise
                        // WebView görünürlüğünü kapatamayız; aksi halde arka plandaki
                        // Meggy yüzeyi siyaha döner. Bu durumda Microsoft'un önerdiği
                        // düşük bellek hedefini kullan. JS tarafı da blur'da kendi render
                        // döngülerini uyuttuğu için standart WebView2 ile beraber GPU/CPU
                        // yükü çok daha düşük kalır.
                        try
                        {
                            _webView.CoreWebView2.MemoryUsageTargetLevel = isInactive
                                ? CoreWebView2MemoryUsageTargetLevel.Low
                                : CoreWebView2MemoryUsageTargetLevel.Normal;
                        }
                        catch { }
                    }
                } while (_powerStateChangePending);
            }
            catch (Exception ex)
            {
                AppendWebLog("power state: " + ex.Message);
            }
            finally
            {
                _powerStateChangeInProgress = false;
            }
        }

        private async void OnWindowPreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            WpfKey actualKey = e.Key == WpfKey.System ? e.SystemKey : e.Key;
            WpfModifierKeys modifiers = WpfKeyboard.Modifiers;

            // Alt+S bir WPF/Windows sistem tuşu olarak yorumlanırsa varsayılan
            // menü uyarı sesi oluşabiliyor. Core bu birleşimi burada sahiplenir,
            // sesi tamamen bastırır ve Map yüzeyini doğrudan WebView'e bildirir.
            if (actualKey == WpfKey.S
                && modifiers == WpfModifierKeys.Alt)
            {
                e.Handled = true;
                try
                {
                    if (_webView?.CoreWebView2 != null)
                    {
                        await _webView.CoreWebView2.ExecuteScriptAsync(
                            "typeof window.__alekToggleWorldMap === 'function' ? window.__alekToggleWorldMap() : true;");
                    }
                }
                catch (Exception ex)
                {
                    AppendWebLog("Alt+S Map dispatch: " + ex.Message);
                }
                return;
            }

            if (actualKey == WpfKey.F1)
            {
                e.Handled = true;
                await OpenShortcutHelpAsync();
                return;
            }

            if (actualKey == WpfKey.F2 && modifiers == WpfModifierKeys.None)
            {
                e.Handled = true;
                try
                {
                    if (_webView?.CoreWebView2 != null)
                    {
                        await _webView.CoreWebView2.ExecuteScriptAsync(
                            "window.__alekOpenPrimaryRadial ? window.__alekOpenPrimaryRadial() : (window.__ALEK_PENDING_PRIMARY_RADIAL__ = true);");
                    }
                }
                catch (Exception ex)
                {
                    AppendWebLog("F2 primary radial dispatch: " + ex.Message);
                }
                return;
            }

            if (actualKey != WpfKey.F11) return;

            if (modifiers == WpfModifierKeys.Shift)
            {
                ToggleTitleBarWorkingSpace();
                e.Handled = true;
            }
            else if (modifiers == WpfModifierKeys.None)
            {
                ToggleWindowedFullscreen();
                e.Handled = true;
            }
        }

        private void OnWindowPreviewKeyUp(object sender, WpfKeyEventArgs e)
        {
            WpfKey actualKey = e.Key == WpfKey.System ? e.SystemKey : e.Key;
            if (actualKey == WpfKey.S
                && (WpfKeyboard.Modifiers & WpfModifierKeys.Alt) == WpfModifierKeys.Alt)
            {
                // KeyDown zaten tek Map geçişini yaptı. KeyUp'ı da işlenmiş
                // saymak Windows'un sonradan uyarı sesi üretmesini engeller.
                e.Handled = true;
            }
        }

        private void OnThreadPreprocessMessage(ref MSG msg, ref bool handled)
        {
            if (handled || msg.message != WmSysChar) return;

            int character = msg.wParam.ToInt32();
            if (character == 's' || character == 'S')
            {
                // Alt+S, WPF PreviewKeyDown tarafından işlense bile Windows ayrıca
                // WM_SYSCHAR gönderebilir. Bu mesaj tüketilmezse klasik sistem
                // uyarı sesi oluşur. Thread düzeyinde tüketmek WebView2 child HWND
                // dâhil bütün pencere zincirinde sesi kesin olarak bastırır.
                handled = true;
            }
        }

        private async Task OpenShortcutHelpAsync()
        {
            try
            {
                if (_webView?.CoreWebView2 == null) return;
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    "window.__alekOpenShortcutHelp ? window.__alekOpenShortcutHelp() : window.postMessage('ALEK_OPEN_SHORTCUT_HELP','*');");
            }
            catch (Exception ex)
            {
                AppendWebLog("F1 shortcut help dispatch: " + ex.Message);
            }
        }

        private void ToggleTitleBarWorkingSpace()
        {
            _isTitleBarAreaInWorkingSpace = !_isTitleBarAreaInWorkingSpace;
            ApplyTitleBarWorkingSpaceMode();
        }

        private void ApplyTitleBarWorkingSpaceMode()
        {
            if (_webView == null) return;

            // Yerleşik üst şerit R38 ile sıfır yüksekliğe alındı; çalışma yüzeyi daima tam alanı kullanır.
            _webView.Margin = _isTitleBarAreaInWorkingSpace
                ? new Thickness(0)
                : new Thickness(0, TitleBarHeight, 0, 0);
        }

        private void AppendWebLog(string message)
        {
            try
            {
                const long maxLogBytes = 1024 * 1024;
                if (File.Exists(_webLogPath) && new FileInfo(_webLogPath).Length >= maxLogBytes)
                    File.WriteAllText(_webLogPath, string.Empty);
                File.AppendAllText(_webLogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
            }
            catch
            {
                // Loglama boyut penceresini asla bozmasın.
            }
        }

        private async Task EnableWebDiagnosticsAsync()
        {
            try
            {
                await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Runtime.enable", "{}");

                _consoleReceiver = _webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.consoleAPICalled");
                _consoleReceiver.DevToolsProtocolEventReceived += OnConsoleApiCalled;

                _exceptionReceiver = _webView.CoreWebView2.GetDevToolsProtocolEventReceiver("Runtime.exceptionThrown");
                _exceptionReceiver.DevToolsProtocolEventReceived += OnRuntimeExceptionThrown;
            }
            catch (Exception ex)
            {
                AppendWebLog("diagnostics unavailable: " + ex.Message);
            }
        }

        private void OnConsoleApiCalled(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            AppendWebLog("console: " + e.ParameterObjectAsJson);
        }

        private void OnRuntimeExceptionThrown(object? sender, CoreWebView2DevToolsProtocolEventReceivedEventArgs e)
        {
            AppendWebLog("exception: " + e.ParameterObjectAsJson);
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                AppendWebLog($"navigation failed: {e.WebErrorStatus}");
            }
        }

        private void OnProcessFailed(object? sender, CoreWebView2ProcessFailedEventArgs e)
        {
            AppendWebLog($"webview process failed: {e.ProcessFailedKind}");
        }

        private void ToggleWindowedFullscreen()
        {
            if (_isWindowedFullscreen)
                RestoreWindow();
            else
                ApplyWindowedFullscreen();
        }

        private void ApplyWindowedFullscreen()
        {
            if (!_isWindowedFullscreen)
            {
                _restoreBounds = new Rect(Left, Top, Width, Height);
            }

            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;

            IntPtr handle = new WindowInteropHelper(this).Handle;
            // F11 gerçek tam ekran olsun: görev çubuğu dahil tüm monitör alanını kapla.
            // Title bar WPF katmanı olarak yerinde kalır; sadece pencere sınırı büyür.
            var area = Forms.Screen.FromHandle(handle).Bounds;
            var source = PresentationSource.FromVisual(this);
            Matrix fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            System.Windows.Point topLeft = fromDevice.Transform(new System.Windows.Point(area.Left, area.Top));
            System.Windows.Point bottomRight = fromDevice.Transform(new System.Windows.Point(area.Right, area.Bottom));

            Left = topLeft.X;
            Top = topLeft.Y;
            Width = bottomRight.X - topLeft.X;
            Height = bottomRight.Y - topLeft.Y;
            _isWindowedFullscreen = true;
        }

        private void RestoreWindow()
        {
            WindowState = WindowState.Normal;
            ResizeMode = ResizeMode.NoResize;
            if (_restoreBounds.Width > 0 && _restoreBounds.Height > 0)
            {
                Left = _restoreBounds.Left;
                Top = _restoreBounds.Top;
                Width = _restoreBounds.Width;
                Height = _restoreBounds.Height;
            }
            _isWindowedFullscreen = false;
        }

        private static bool ReadAlekWindowCommand(string jsPath)
        {
            try
            {
                if (!File.Exists(jsPath)) return false;
                string head = string.Join("\n", File.ReadLines(jsPath).Take(80));
                return Regex.IsMatch(head, @"alek\.window\.fullscreen\s*=\s*true", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(head, @"""fullscreen""\s*:\s*true", RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        // ============================================================
        // GÜVENLİ PATH ÇÖZÜCÜ
        // Relative path'i alır, root klasörle birleştirir, sandbox dışına
        // çıkış denemelerini engeller. Güvensizse null döner.
        // ============================================================
        private string? ResolveSafePath(string? relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            // Absolute path, farklı disk veya UNC engelle
            if (Path.IsPathRooted(relativePath)) return null;

            try
            {
                string combined = Path.GetFullPath(Path.Combine(_rootFolder, relativePath));

                // Root dışına çıkış kontrolü (trailing separator sayesinde prefix collision önlenir)
                if (!combined.StartsWith(_rootFolder, StringComparison.OrdinalIgnoreCase))
                    return null;

                return combined;
            }
            catch
            {
                return null; // Geçersiz karakterler vs.
            }
        }

        // ============================================================
        // C# → JS RESPONSE GÖNDERİCİ
        // ============================================================
        private void SendResponse(string requestId, object result)
        {
            try
            {
                string json = JsonSerializer.Serialize(result);
                string idEscaped = JsonSerializer.Serialize(requestId);
                string script = $"window.__alekDispatch({idEscaped},{json})";
                _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Response hatası: " + ex.Message);
            }
        }

        // ============================================================
        // HOST KAPANIŞI
        // app.exit, Promise tabanlı veya id'siz legacy mesaj olarak gelebilir.
        // Kapanış WebView/renderer callback'ine bırakılmaz; host doğrudan pencereyi kapatır.
        // ============================================================
        private void HandleHostExitRequest(string? requestId)
        {
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                SendResponse(requestId, new { ok = true, closing = true });
            }

            if (_hostExitRequested)
                return;

            _hostExitRequested = true;
            AppendWebLog("app.exit: host shutdown requested");

            // Normal yol başarısız olursa süreç Ay ekranında sonsuza kadar kalmasın.
            // OnClosed çalıştığında bayrak true olur ve bu emniyet yolu hiçbir şey yapmaz.
            _ = Task.Run(async () =>
            {
                await Task.Delay(1500).ConfigureAwait(false);
                if (_hostExitClosed)
                    return;

                try
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (!_hostExitClosed)
                            Application.Current.Shutdown();
                    });
                }
                catch
                {
                    // Bir sonraki aşama process fallback'tir.
                }

                await Task.Delay(650).ConfigureAwait(false);
                if (_hostExitClosed)
                    return;

                try
                {
                    Environment.Exit(0);
                }
                catch
                {
                    // Process zaten kapanıyor olabilir.
                }
            });

            void CloseNow()
            {
                if (_hostExitClosed)
                    return;

                try
                {
                    // WebMessageReceived zaten UI thread'de çalışır. BeginInvoke kullanmak
                    // çıkışın renderer/dispatcher yaşam döngüsünün arkasında kalmasına yol açabiliyordu.
                    // Close burada senkron olarak çağrılır.
                    Close();
                }
                catch (Exception ex)
                {
                    AppendWebLog("app.exit Close failed: " + ex);
                    try
                    {
                        Application.Current?.Shutdown();
                    }
                    catch
                    {
                        try { Environment.Exit(0); } catch { }
                    }
                }
            }

            if (Dispatcher.CheckAccess())
            {
                CloseNow();
            }
            else
            {
                Dispatcher.Invoke(
                    new Action(CloseNow),
                    System.Windows.Threading.DispatcherPriority.Send);
            }
        }

        // ============================================================
        // JS -> C# KÖPRÜSÜ
        // Eski mesajlar (id'siz): { "op":"save", "data":"..." } → geriye uyumlu
        // Yeni mesajlar (id'li):  { "op":"fs.readText", "id":"req_1", "payload":{...} }
        // ============================================================
        private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                string raw = GetWebMessagePayload(e);
                using var doc = JsonDocument.Parse(raw);
                if (!doc.RootElement.TryGetProperty("op", out var opEl)) return;
                string op = opEl.GetString() ?? "";

                string? requestId = null;
                if (doc.RootElement.TryGetProperty("id", out var requestIdEl) &&
                    requestIdEl.ValueKind == JsonValueKind.String)
                {
                    requestId = requestIdEl.GetString();
                }

                // app.exit host seviyesinde özel komuttur. Promise id'si olsun veya olmasın
                // API servis zincirine girmeden doğrudan pencere kapanışına gider.
                if (string.Equals(op, "app.exit", StringComparison.OrdinalIgnoreCase))
                {
                    HandleHostExitRequest(requestId);
                    return;
                }

                // Yeni API sistemi: id varsa Promise tabanlı response
                if (requestId != null)
                {
                    JsonElement payload = doc.RootElement.TryGetProperty("payload", out var pEl)
                        ? pEl : default;
                    HandleApiRequest(op, requestId, payload);
                    return;
                }

                // ====== ESKİ SİSTEM (GERİYE UYUMLU) ======
                if (op == "save")
                {
                    string data = doc.RootElement.TryGetProperty("data", out var dEl)
                        ? (dEl.GetString() ?? "{}") : "{}";
                    // Atomik yazma: önce .tmp'a, sonra rename
                    string tmp = _memoryPath + ".tmp";
                    File.WriteAllText(tmp, data);
                    if (File.Exists(_memoryPath)) File.Delete(_memoryPath);
                    File.Move(tmp, _memoryPath);
                }
                else if (op == "reveal")
                {
                    // Hafıza dosyasını Windows Gezgini'nde aç ve seç
                    if (File.Exists(_memoryPath))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{_memoryPath}\"");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Köprü hatası: " + ex.Message);
            }
        }

        private static string GetWebMessagePayload(CoreWebView2WebMessageReceivedEventArgs e)
        {
            using var envelope = JsonDocument.Parse(e.WebMessageAsJson);
            if (envelope.RootElement.ValueKind == JsonValueKind.String)
                return envelope.RootElement.GetString() ?? "";

            return e.WebMessageAsJson;
        }

        // ============================================================
        // YENİ API İSTEK İŞLEYİCİ — 9 OPERASYON
        // ============================================================
        private void HandleApiRequest(string op, string reqId, JsonElement payload)
        {
            try
            {
                if (TryHandleSafeAiApi(op, reqId, payload))
                    return;

                // Meggy taşınabilir SQLite katmanı. Kayıt listesi Data/meggy.db,
                // her maceranın bütün oyun durumu Games/<macera>/game.db içindedir.
                if (_gameStore.TryHandleApi(op, payload, out object portableDbResult))
                {
                    SendResponse(reqId, portableDbResult);
                    return;
                }

                // Sürümden bağımsız kullanıcı verisi kasası. Seçici yalnız
                // kullanıcının açıkça başlattığı içe/dışa aktarma sırasında açılır.
                if (_dataTransferService.TryHandleApi(op, payload, out object dataTransferResult))
                {
                    SendResponse(reqId, dataTransferResult);
                    return;
                }

                // Core: Windows dosya seçicisi + harici medya denetimi.
                // Bu iki işlem sandbox dosya API'sinden ayrıdır; yalnız kullanıcı
                // tarafından seçilmiş medya yollarına izin verir.
                if (GraphicsBridge.TryHandleApi(op, payload, out object graphicsResult))
                {
                    SendResponse(reqId, graphicsResult);
                    return;
                }

                if (ExternalMediaBridge.TryHandleApi(
                    _webView.CoreWebView2,
                    this,
                    op,
                    payload,
                    out object externalMediaResult))
                {
                    SendResponse(reqId, externalMediaResult);
                    return;
                }

                if (_developerBridge.TryHandleApi(op, payload, out object developerResult))
                {
                    SendResponse(reqId, developerResult);
                    return;
                }

                string relPath = "";
                if (payload.ValueKind == JsonValueKind.Object &&
                    payload.TryGetProperty("path", out var pathEl))
                {
                    relPath = pathEl.GetString() ?? "";
                }

                switch (op)
                {
                    // ─── app.exit ───
                    case "app.exit":
                    {
                        // Normalde OnWebMessageReceived bu komutu daha önce yakalar.
                        // Burada bırakılan yol doğrudan çağrılar için aynı sağlam davranışı korur.
                        HandleHostExitRequest(reqId);
                        return;
                    }

                    // ─── fs.list ───
                    case "fs.list":
                    {
                        string? safePath = string.IsNullOrEmpty(relPath) ? _rootFolder.TrimEnd(Path.DirectorySeparatorChar) : ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }
                        if (!Directory.Exists(safePath)) { SendResponse(reqId, new { ok = false, error = "not_found" }); return; }

                        var items = new List<object>();
                        foreach (var d in Directory.GetDirectories(safePath))
                            items.Add(new { name = Path.GetFileName(d), type = "directory" });
                        foreach (var f in Directory.GetFiles(safePath))
                            items.Add(new { name = Path.GetFileName(f), type = "file", size = new FileInfo(f).Length });

                        SendResponse(reqId, new { ok = true, items });
                        return;
                    }

                    // ─── fs.readText ───
                    case "fs.readText":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }
                        if (!File.Exists(safePath)) { SendResponse(reqId, new { ok = false, error = "not_found" }); return; }

                        string content = File.ReadAllText(safePath, System.Text.Encoding.UTF8);
                        SendResponse(reqId, new { ok = true, content });
                        return;
                    }

                    // ─── fs.writeText ───
                    case "fs.writeText":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        string content = "";
                        if (payload.ValueKind == JsonValueKind.Object &&
                            payload.TryGetProperty("content", out var cEl))
                            content = cEl.GetString() ?? "";

                        string? dir = Path.GetDirectoryName(safePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        // BOM'suz + atomik yazma: önce .tmp'a yaz, sonra tek adımda yerine taşı.
                        // Böylece yazma sırasında çökme/güç kesintisi olsa bile hedef dosya
                        // ya eski ya yeni tam haliyle kalır; ayrıca BOM eklenmez.
                        string tmpPath = safePath + ".tmp";
                        File.WriteAllText(tmpPath, content, new System.Text.UTF8Encoding(false));
                        File.Move(tmpPath, safePath, true);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.readJson ───
                    case "fs.readJson":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }
                        if (!File.Exists(safePath)) { SendResponse(reqId, new { ok = false, error = "not_found" }); return; }

                        string raw = File.ReadAllText(safePath, System.Text.Encoding.UTF8);
                        try
                        {
                            using var jsonDoc = JsonDocument.Parse(raw);
                            // Geçerli JSON — ham metni data olarak gönder (JS tarafında parse edilecek)
                            string jsonStr = JsonSerializer.Serialize(new { ok = true });
                            // data alanını ham JSON olarak ekle
                            jsonStr = jsonStr.TrimEnd('}') + ",\"data\":" + raw + "}";
                            string idEsc = JsonSerializer.Serialize(reqId);
                            _webView.CoreWebView2.ExecuteScriptAsync($"window.__alekDispatch({idEsc},{jsonStr})");
                        }
                        catch (JsonException)
                        {
                            SendResponse(reqId, new { ok = false, error = "invalid_json" });
                        }
                        return;
                    }

                    // ─── fs.writeJson ───
                    case "fs.writeJson":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        string jsonContent = "{}";
                        if (payload.ValueKind == JsonValueKind.Object &&
                            payload.TryGetProperty("data", out var dataEl))
                        {
                            jsonContent = JsonSerializer.Serialize(dataEl, new JsonSerializerOptions { WriteIndented = true });
                        }

                        string? dir = Path.GetDirectoryName(safePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        // BOM'suz + atomik yazma: önce .tmp'a yaz, sonra tek adımda yerine taşı.
                        string tmpPath = safePath + ".tmp";
                        File.WriteAllText(tmpPath, jsonContent, new System.Text.UTF8Encoding(false));
                        File.Move(tmpPath, safePath, true);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.exists ───
                    case "fs.exists":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        if (File.Exists(safePath))
                            SendResponse(reqId, new { ok = true, exists = true, type = "file" });
                        else if (Directory.Exists(safePath))
                            SendResponse(reqId, new { ok = true, exists = true, type = "directory" });
                        else
                            SendResponse(reqId, new { ok = true, exists = false });
                        return;
                    }

                    // ─── fs.mkdir ───
                    case "fs.mkdir":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        Directory.CreateDirectory(safePath);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.delete ───
                    case "fs.delete":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        if (Directory.Exists(safePath))
                        {
                            // Güvenlik: klasör silme kapalı
                            SendResponse(reqId, new { ok = false, error = "directory_delete_disabled" });
                            return;
                        }
                        if (!File.Exists(safePath))
                        {
                            SendResponse(reqId, new { ok = false, error = "not_found" });
                            return;
                        }

                        File.Delete(safePath);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.move ───
                    case "fs.move":
                    {
                        string sourceRel = "";
                        string destinationRel = "";
                        if (payload.ValueKind == JsonValueKind.Object)
                        {
                            if (payload.TryGetProperty("source", out var sourceEl)) sourceRel = sourceEl.GetString() ?? "";
                            if (payload.TryGetProperty("destination", out var destinationEl)) destinationRel = destinationEl.GetString() ?? "";
                        }
                        string? sourcePath = ResolveSafePath(sourceRel);
                        string? destinationPath = ResolveSafePath(destinationRel);
                        if (sourcePath == null || destinationPath == null)
                        {
                            SendResponse(reqId, new { ok = false, error = "invalid_path" });
                            return;
                        }
                        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
                        {
                            SendResponse(reqId, new { ok = true });
                            return;
                        }
                        if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                        {
                            SendResponse(reqId, new { ok = false, error = "destination_exists" });
                            return;
                        }
                        string? destinationParent = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(destinationParent)) Directory.CreateDirectory(destinationParent);
                        if (Directory.Exists(sourcePath)) Directory.Move(sourcePath, destinationPath);
                        else if (File.Exists(sourcePath)) File.Move(sourcePath, destinationPath);
                        else
                        {
                            SendResponse(reqId, new { ok = false, error = "not_found" });
                            return;
                        }
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.deleteTree ───
                    case "fs.deleteTree":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null)
                        {
                            SendResponse(reqId, new { ok = false, error = "invalid_path" });
                            return;
                        }
                        string rootPath = _rootFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        string dataRoot = Path.GetFullPath(Path.Combine(_rootFolder, "Data"));
                        if (string.Equals(safePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), rootPath, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(safePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), dataRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                        {
                            SendResponse(reqId, new { ok = false, error = "protected_path" });
                            return;
                        }
                        if (!Directory.Exists(safePath))
                        {
                            SendResponse(reqId, new { ok = false, error = "not_found" });
                            return;
                        }
                        Directory.Delete(safePath, true);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── fs.writeBinary ───
                    case "fs.writeBinary":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null) { SendResponse(reqId, new { ok = false, error = "invalid_path" }); return; }

                        string base64 = "";
                        if (payload.ValueKind == JsonValueKind.Object &&
                            payload.TryGetProperty("base64", out var b64El))
                            base64 = b64El.GetString() ?? "";

                        if (string.IsNullOrEmpty(base64)) { SendResponse(reqId, new { ok = false, error = "empty_data" }); return; }

                        string? dir = Path.GetDirectoryName(safePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        byte[] bytes = Convert.FromBase64String(base64);
                        File.WriteAllBytes(safePath, bytes);
                        SendResponse(reqId, new { ok = true });
                        return;
                    }

                    // ─── alek.load ───
                    case "alek.load":
                    {
                        string? safePath = ResolveSafePath(relPath);
                        if (safePath == null || !safePath.EndsWith(".alek", StringComparison.OrdinalIgnoreCase))
                        {
                            SendResponse(reqId, new { ok = false, error = "invalid_path" });
                            return;
                        }
                        if (!File.Exists(safePath))
                        {
                            SendResponse(reqId, new { ok = false, error = "not_found" });
                            return;
                        }

                        // Hedef .alek dosyasına geç
                        _jsPath = safePath;
                        _memoryPath = safePath + ".json";
                        // _rootFolder DEĞİŞMEZ — aynı sandbox içinde kalır
                        ExternalMediaBridge.RefreshApprovedPaths(_webView.CoreWebView2, _memoryPath);

                        // Sayfayı yeniden yükle
                        Dispatcher.Invoke(() => FireUpJs());
                        // Response gönderilmez — sayfa yeniden yüklenecek
                        return;
                    }

                    default:
                        SendResponse(reqId, new { ok = false, error = "unknown_op" });
                        return;
                }
            }
            catch (Exception ex)
            {
                SendResponse(reqId, new { ok = false, error = ex.Message });
            }
        }

        private bool TryHandleSafeAiApi(string op, string reqId, JsonElement payload)
        {
            if (!op.StartsWith("safeAi.", StringComparison.Ordinal))
                return false;

            try
            {
                switch (op)
                {
                    case "safeAi.open":
                    {
                        ApplySafeAiBounds(payload);
                        _ = OpenEdgeChatGptAsync(reqId);
                        return true;
                    }

                    case "safeAi.bounds":
                        ApplySafeAiBounds(payload);
                        SendResponse(reqId, new { ok = true });
                        return true;

                    case "safeAi.close":
                        _edgeChatGptDock.Hide();
                        SendResponse(reqId, new { ok = true });
                        return true;

                    default:
                        SendResponse(reqId, new { ok = false, error = "unknown_safe_ai_op" });
                        return true;
                }
            }
            catch (Exception ex)
            {
                SendResponse(reqId, new { ok = false, error = ex.Message });
                return true;
            }
        }

        private async Task OpenEdgeChatGptAsync(string reqId)
        {
            try
            {
                await _edgeChatGptDock.OpenAsync();
                SendResponse(reqId, new
                {
                    ok = true,
                    mode = "microsoft_edge_app_dock",
                    bridge = "not_connected",
                    pageAccess = "none",
                    destination = "https://chatgpt.com/"
                });
            }
            catch (Exception ex)
            {
                SendResponse(reqId, new { ok = false, error = ex.Message });
            }
        }

        private void ApplySafeAiBounds(JsonElement payload)
        {
            JsonElement bounds = payload;
            if (payload.ValueKind == JsonValueKind.Object &&
                payload.TryGetProperty("bounds", out JsonElement boundsElement) &&
                boundsElement.ValueKind == JsonValueKind.Object)
            {
                bounds = boundsElement;
            }

            double left = ReadPayloadDouble(bounds, "left", 0);
            double top = ReadPayloadDouble(bounds, "top", 116);
            double width = ReadPayloadDouble(bounds, "width", Math.Max(640, ActualWidth));
            double height = ReadPayloadDouble(bounds, "height", Math.Max(420, ActualHeight - top));
            _safeAiCssBounds = new Rect(
                Math.Max(0, left),
                Math.Max(0, top),
                Math.Max(1, width),
                Math.Max(1, height));
            _safeAiCssBoundsZoomFactor = ReadWebViewZoomFactor();
            _hasSafeAiCssBounds = true;
            ApplyScaledSafeAiBounds();
        }

        private void ApplyScaledSafeAiBounds()
        {
            if (!_hasSafeAiCssBounds)
                return;

            // JavaScript getBoundingClientRect() değerlerini CSS pikseliyle
            // gönderir. WebView2 Ctrl+tekerlek yakınlaştırmasında bir CSS
            // pikseli artık bir WPF DIP değildir; ZoomFactor kadar büyür.
            // Edge ayrı bir üst seviye pencere olduğundan bu dönüşümü burada
            // yapmazsak Meggy yakınlaşır fakat AI yüzeyi eski oranda kalır.
            _edgeChatGptDock.ApplyBounds(
                _safeAiCssBounds.Left * _safeAiCssBoundsZoomFactor,
                _safeAiCssBounds.Top * _safeAiCssBoundsZoomFactor,
                _safeAiCssBounds.Width * _safeAiCssBoundsZoomFactor,
                _safeAiCssBounds.Height * _safeAiCssBoundsZoomFactor);
        }

        private double ReadWebViewZoomFactor()
        {
            double zoomFactor = _webView?.ZoomFactor ?? 1.0;
            return double.IsFinite(zoomFactor) && zoomFactor > 0
                ? zoomFactor
                : 1.0;
        }

        private async void OnWebViewZoomFactorChanged(object? sender, EventArgs e)
        {
            int generation = ++_safeAiZoomSyncGeneration;
            // Eski rect, eski ZoomFactor ile birlikte fiziksel olarak hâlâ
            // doğrudur. Taze CSS rect gelene kadar onu yanlış yeni katsayıyla
            // çarpıp tek karelik sıçrama üretme.
            ApplyScaledSafeAiBounds();

            try
            {
                // Chromium yakınlaştırma sonrasında layout'u birkaç karede
                // tamamlayabilir. Önce mevcut CSS sınırlarını doğru katsayıyla
                // uygula, ardından sayfadan taze rect iste ve son kez pekiştir.
                await Task.Delay(40);
                if (generation != _safeAiZoomSyncGeneration)
                    return;

                ApplyScaledSafeAiBounds();
                if (_webView?.CoreWebView2 != null)
                {
                    await _webView.CoreWebView2.ExecuteScriptAsync(
                        "requestAnimationFrame(()=>requestAnimationFrame(()=>window.__alekSyncSafeAiBounds?.()));");
                }

                await Task.Delay(120);
                if (generation == _safeAiZoomSyncGeneration)
                    ApplyScaledSafeAiBounds();
            }
            catch (Exception ex)
            {
                AppendWebLog("safe AI zoom sync: " + ex.Message);
            }
        }

        private static double ReadPayloadDouble(JsonElement payload, string name, double fallback)
        {
            if (payload.ValueKind != JsonValueKind.Object ||
                !payload.TryGetProperty(name, out JsonElement value))
            {
                return fallback;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out double number))
                return number;

            if (value.ValueKind == JsonValueKind.String &&
                double.TryParse(
                    value.GetString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out number))
            {
                return number;
            }

            return fallback;
        }

        // ============================================================
        // BLUEMOON.PNG → VIRTUAL HOST URL
        // ============================================================
        private string GetBluemoonUrl()
        {
            // Resources klasöründen virtual host üzerinden sun
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Bluemoon.png");
            if (File.Exists(localPath))
            {
                return "https://alek-engine.local/Bluemoon.png";
            }
            // Fallback: base64 data URL
            try
            {
                var uri = new Uri("pack://application:,,,/Resources/Bluemoon.png", UriKind.Absolute);
                StreamResourceInfo sri = System.Windows.Application.GetResourceStream(uri);
                if (sri != null)
                {
                    using var ms = new MemoryStream();
                    sri.Stream.CopyTo(ms);
                    string b64 = Convert.ToBase64String(ms.ToArray());
                    return "data:image/png;base64," + b64;
                }
            }
            catch { }
            return "";
        }

        // ============================================================
        // RESİM KEŞFİ VE NUMERİC SIRALAMA
        // ============================================================
        private List<string> DiscoverSlides(string alekFolder)
        {
            // Sunum Resim klasörüne bak, yoksa .alek klasörünü kullan
            string imageFolder = Path.Combine(alekFolder, "Sunum Resim");
            if (!Directory.Exists(imageFolder))
            {
                imageFolder = alekFolder;
            }

            var files = Directory.GetFiles(imageFolder)
                .Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();

            // Numeric sort: dosya adındaki ilk sayıyı yakala
            var regex = new Regex(@"\d+");
            files.Sort((a, b) =>
            {
                string nameA = Path.GetFileName(a);
                string nameB = Path.GetFileName(b);
                var matchA = regex.Match(nameA);
                var matchB = regex.Match(nameB);

                long numA = matchA.Success ? long.Parse(matchA.Value) : long.MaxValue;
                long numB = matchB.Success ? long.Parse(matchB.Value) : long.MaxValue;

                int cmp = numA.CompareTo(numB);
                if (cmp != 0) return cmp;

                // Aynı ilk sayıda ikincil sıralama: dosya adı alfabetik
                return string.Compare(nameA, nameB, StringComparison.OrdinalIgnoreCase);
            });

            return files;
        }

        // ============================================================
        // MÜZİK KEŞFİ
        // ============================================================
        private string? DiscoverAudio(string alekFolder)
        {
            // Önce .alek dosyasının bulunduğu klasörde ara
            var audioFile = FindFirstAudio(alekFolder);
            if (audioFile != null) return audioFile;

            // Yoksa Sunum Resim klasöründe ara
            string imageFolder = Path.Combine(alekFolder, "Sunum Resim");
            if (Directory.Exists(imageFolder))
            {
                audioFile = FindFirstAudio(imageFolder);
            }
            return audioFile;
        }

        private string? FindFirstAudio(string folder)
        {
            try
            {
                return Directory.GetFiles(folder)
                    .Where(f => AudioExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }
            catch { return null; }
        }

        // ============================================================
        // DOSYA YOLUNU VIRTUAL HOST URL'İNE ÇEVİR
        // ============================================================
        private string ToVirtualUrl(string filePath, string alekFolder)
        {
            // alekFolder'a göre göreli yolu al
            string relativePath = Path.GetRelativePath(alekFolder, filePath);
            // Windows path separator'ı URL separator'a çevir
            string[] segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            // Her segmenti ayrı ayrı encode et (boşluklar, Türkçe karakterler vs.)
            string encodedPath = string.Join("/", segments.Select(seg => Uri.EscapeDataString(seg)));
            return "https://alek-assets.local/" + encodedPath;
        }

        // ============================================================
        // ANA MOTOR: JS ATEŞLEME
        // ============================================================
        private void FireUpJs()
        {
            if (!File.Exists(_jsPath)) return;
            _edgeChatGptDock.Hide();
            string jsContent = File.ReadAllText(_jsPath);
            string alekFolder = Path.GetDirectoryName(Path.GetFullPath(_jsPath)) ?? "";

            // ====== HAFIZAYI DİSKTEN OKU (varsa) ======
            string memoryJson = "null";
            try
            {
                if (File.Exists(_memoryPath))
                {
                    string raw = File.ReadAllText(_memoryPath);
                    if (!string.IsNullOrWhiteSpace(raw))
                    {
                        // Geçerli JSON mu kontrol et; geçerliyse aynen ver, değilse null
                        using var _ = JsonDocument.Parse(raw);
                        memoryJson = raw;
                    }
                }
            }
            catch { memoryJson = "null"; }

            // Ham JSON metnini JS tek-tırnaklı string literal'i içinde güvenle gömmek için kaçır.
            // Sıra önemli: önce ters slash, sonra diğerleri.
            string memoryJsForLiteral = (memoryJson ?? "null")
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\u2028", "\\u2028") // U+2028 LINE SEPARATOR — JS literal'inde kabul edilmez
                .Replace("\u2029", "\\u2029") // U+2029 PARAGRAPH SEPARATOR
                .Replace("</", "<\\/");        // erken </script> kapanışı koruması

            // ====== ASSET KEŞFİ ======
            string moonDataUrl = GetBluemoonUrl();

            // Resim keşfi
            List<string> slideFiles = DiscoverSlides(alekFolder);
            var slideUrls = slideFiles.Select(f => ToVirtualUrl(f, alekFolder)).ToList();
            string slidesArrayJs = "[" + string.Join(",", slideUrls.Select(u => "\"" + EscapeJsString(u) + "\"")) + "]";

            // Müzik keşfi
            string? audioFile = DiscoverAudio(alekFolder);
            string audioJs;
            string audioNameJs;
            if (audioFile != null)
            {
                string audioUrl = ToVirtualUrl(audioFile, alekFolder);
                audioJs = "\"" + EscapeJsString(audioUrl) + "\"";
                audioNameJs = "\"" + EscapeJsString(Path.GetFileName(audioFile)) + "\"";
            }
            else
            {
                audioJs = "null";
                audioNameJs = "null";
            }

            string assetsInjector = $@"
                window.__alekAssets = {{
                    moon: ""{EscapeJsString(moonDataUrl)}"",
                    audio: {audioJs},
                    audioName: {audioNameJs},
                    slides: {slidesArrayJs},
                    slideCount: {slideUrls.Count}
                }};
            ";

            string injector = @"
                // ====== ALEKRYTHAE KÖPRÜSÜ (JS<->C#) ======
                window.__alekStorage = (function() {
                    try { return JSON.parse('" + memoryJsForLiteral + @"'); } catch(e) { return null; }
                })();
                window.__alekSave = function(jsonString) {
                    try {
                        if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                            window.chrome.webview.postMessage({ op: 'save', data: jsonString });
                            return true;
                        }
                    } catch(e) { console.error('alekSave köprü hatası:', e); }
                    return false;
                };
                window.__alekReveal = function() {
                    try {
                        if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {
                            window.chrome.webview.postMessage({ op: 'reveal' });
                        }
                    } catch(e) {}
                };
                // ==========================================

                // ====== YENİ DOSYA SİSTEMİ KÖPRÜSÜ (Promise tabanlı) ======
                window.__alekPending = {};
                window.__alekReqId = 0;

                window.__alekDispatch = function(id, result) {
                    if (window.__alekPending[id]) {
                        window.__alekPending[id](result);
                        delete window.__alekPending[id];
                    }
                };

                window.__alekAPI = function(op, payload) {
                    return new Promise(function(resolve) {
                        var id = '__alek_' + (++window.__alekReqId);
                        window.__alekPending[id] = resolve;
                        try {
                            window.chrome.webview.postMessage(JSON.stringify({ op: op, id: id, payload: payload || {} }));
                        } catch(e) {
                            delete window.__alekPending[id];
                            resolve({ ok: false, error: 'bridge_unavailable' });
                        }
                    });
                };

                // Yardımcı fonksiyonlar
                window.__alekReadText = function(p) { return window.__alekAPI('fs.readText', { path: p }); };
                window.__alekWriteText = function(p, c) { return window.__alekAPI('fs.writeText', { path: p, content: c }); };
                window.__alekReadJson = function(p) { return window.__alekAPI('fs.readJson', { path: p }); };
                window.__alekWriteJson = function(p, d) { return window.__alekAPI('fs.writeJson', { path: p, data: d }); };
                window.__alekList = function(p) { return window.__alekAPI('fs.list', { path: p || '' }); };
                window.__alekExists = function(p) { return window.__alekAPI('fs.exists', { path: p }); };
                window.__alekMkdir = function(p) { return window.__alekAPI('fs.mkdir', { path: p }); };
                window.__alekDelete = function(p) { return window.__alekAPI('fs.delete', { path: p }); };
                window.__alekMove = function(source, destination) { return window.__alekAPI('fs.move', { source: source, destination: destination }); };
                window.__alekDeleteTree = function(p) { return window.__alekAPI('fs.deleteTree', { path: p }); };
                window.__alekWriteBinary = function(p, b64) { return window.__alekAPI('fs.writeBinary', { path: p, base64: b64 }); };
                window.__alekLoad = function(p) { return window.__alekAPI('alek.load', { path: p }); };
                window.__alekRegistryRead = function() { return window.__alekAPI('db.registry.read', {}); };
                window.__alekRegistryWrite = function(data) { return window.__alekAPI('db.registry.write', { data: data }); };
                window.__alekGameEnsure = function(folder, mode) { return window.__alekAPI('db.game.ensure', { folder: folder, mode: mode }); };
                window.__alekGameRead = function(folder, key) { return window.__alekAPI('db.game.read', { folder: folder, key: key }); };
                window.__alekGameWrite = function(folder, key, data) { return window.__alekAPI('db.game.write', { folder: folder, key: key, data: data }); };
                window.__alekGameHasDocument = function(folder, key) { return window.__alekAPI('db.game.hasDocument', { folder: folder, key: key }); };
                window.__alekGameDelete = function(folder) { return window.__alekAPI('db.game.delete', { folder: folder }); };
                window.__alekGameRename = function(oldFolder, newFolder) { return window.__alekAPI('db.game.rename', { oldFolder: oldFolder, newFolder: newFolder }); };
                window.__alekDataExport = function() { return window.__alekAPI('data.export', {}); };
                window.__alekDataImport = function() { return window.__alekAPI('data.import', {}); };

                // Core harici medya yardımcıları.
                // Dış yol yalnız dosya seçimi ve paket içine kopyalama süresince
                // geçicidir; kalıcı oyun kaydına mutlak Windows yolu yazılmaz.
                window.__alekMediaLinkMode = 'portable-copy';
                window.__alekPickExternalMedia = function(options) {
                    return window.__alekAPI('pickExternalMedia', options || {});
                };
                window.__alekProbeExternalMedia = function(path) {
                    return window.__alekAPI('probeExternalMedia', { path: path });
                };
                window.__alekExternalMediaUrl = function(path) {
                    if (!path) return '';
                    return 'https://alek-external.local/media?path=' + encodeURIComponent(String(path));
                };
                // ===========================================================

                const style = document.createElement('style');
                style.innerHTML = '.alek-msg { position:fixed; top:20px; left:50%; transform:translateX(-50%); background:rgba(5,10,20,0.95); border:1px solid #3A7BD5; color:#E0E0E0; padding:15px 30px; border-radius:8px; box-shadow: 0 0 20px rgba(58,123,213,0.5); font-family:Consolas; z-index:9999; }';
                document.head.appendChild(style);
                // Not: window.Notice'i .alek dosyası kendi shim'inde override edecek;
                // burada sadece fallback olarak duruyor (script yüklenmezse diye).
                if (typeof window.Notice !== 'function') {
                    window.alert = window.Notice = function(msg) {
                        const b = document.createElement('div'); b.className='alek-msg'; b.innerText=msg; document.body.appendChild(b);
                        setTimeout(() => b.remove(), 4000);
                    };
                }
            ";

            string safeJsContent = Regex.Replace(jsContent, "</script", "<\\/script", RegexOptions.IgnoreCase);
            string html = $"<!doctype html><html><head><meta charset='utf-8'></head><body style='background:transparent; color:white; font-family:sans-serif; margin:0; padding:20px;'><script>{injector}</script><script>{assetsInjector}</script><script type='module'>{safeJsContent}</script></body></html>";
            _webView.NavigateToString(html);
        }

        // ============================================================
        // JS STRING ESCAPE HELPER
        // ============================================================
        private static string EscapeJsString(string s)
        {
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("'", "\\'")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n")
                .Replace("\u2028", "\\u2028")
                .Replace("\u2029", "\\u2029")
                .Replace("</", "<\\/");
        }

        protected override void OnClosed(EventArgs e)
        {
            _hostExitClosed = true;
            base.OnClosed(e);
            if (_altSSystemCharacterHooked)
            {
                try { ComponentDispatcher.ThreadPreprocessMessage -= OnThreadPreprocessMessage; } catch { }
                _altSSystemCharacterHooked = false;
            }
            _safeAiZoomSyncGeneration++;
            try
            {
                if (_webViewZoomHooked)
                    _webView.ZoomFactorChanged -= OnWebViewZoomFactorChanged;
            }
            catch { }
            _webViewZoomHooked = false;
            try { _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived; } catch { }
            try { _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted; } catch { }
            try { _webView.CoreWebView2.ProcessFailed -= OnProcessFailed; } catch { }
            try { if (_consoleReceiver != null) _consoleReceiver.DevToolsProtocolEventReceived -= OnConsoleApiCalled; } catch { }
            try { if (_exceptionReceiver != null) _exceptionReceiver.DevToolsProtocolEventReceived -= OnRuntimeExceptionThrown; } catch { }
            try { _developerBridge.Dispose(); } catch { }
            try { _edgeChatGptDock.Dispose(); } catch { }
            _webView.Dispose();
        }
    }
}
