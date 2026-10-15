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
using AlekrythaeCore.Storage;
using AlekrythaeCore.Bridge;
using Forms = System.Windows.Forms;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;
using WpfKeyboard = System.Windows.Input.Keyboard;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;

namespace AlekrythaeCore
{
    public partial class CosmicGate : Window
    {
        private WebView2 _webView;
        private bool _hostExitRequested;
        private bool _isClosing;
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
        private readonly Diagnostics.BufferedFileLog _webLog = new(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alekrythae_webview.log"));
        private CoreWebView2DevToolsProtocolEventReceiver? _consoleReceiver;
        private CoreWebView2DevToolsProtocolEventReceiver? _exceptionReceiver;
        private bool _webViewSuspended;
        private bool _powerStateChangeInProgress;
        private bool _powerStateChangePending;
        private Rect _safeAiCssBounds;
        private bool _hasSafeAiCssBounds;
        private bool _webViewZoomHooked;
        private int _safeAiZoomSyncGeneration;
        private bool _altSSystemCharacterHooked;
        private ViodCeraBridge? _viodCeraBridge;
        private readonly bool _isViodCeraApp;
        private const int WmSysChar = 0x0106;
        // R9: WebView2 WPF NavigateToString, generated HTML'i dahili bir data:text/html
        // navigasyonu olarak başlatabiliyor. Top-level navigation kilidi bu tek, Core tarafından
        // başlatılmış navigasyonu güvenli bir zaman penceresinde bir kez kabul eder.
        private bool _allowGeneratedDocumentNavigation;
        private DateTime _generatedDocumentNavigationDeadlineUtc = DateTime.MinValue;

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
            _paths = new SandboxPathPolicy(_rootFolder);
            _storageLease = new RootStorageLease(_rootFolder);
            _storageServices = new IStorageApiService[] { new FileApiService(_paths), _gameStore };
            _requests = new SerialWorkQueue(ex => AppendWebLog("bridge worker failed: " + ex));
            _dataTransferService = new DataTransferService(_rootFolder, this);
            _developerBridge = new DeveloperBridge();
            _startFullscreen = ReadAlekWindowCommand(jsPath);
            _isViodCeraApp = IsViodCeraApplication(jsPath);

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
            this.Title = Program.DisplayName + " - " + Path.GetFileName(jsPath);
            this.Width = _isViodCeraApp ? 560 : 1024;
            this.Height = _isViodCeraApp ? 430 : 768;
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
            // Chrome, chatgpt.com'u uygulama modunda açar ve yalnız pencere düzeyinde
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
                    if (_isClosing) return;

                    var environmentOptions = new CoreWebView2EnvironmentOptions
                    {
                        AdditionalBrowserArguments = GraphicsBridge.GetAdditionalBrowserArguments()
                    };

                    var env = await CoreWebView2Environment.CreateAsync(
                        null,
                        _engineDataFolder,
                        environmentOptions);

                    // Pencere WebView2 environment hazırlanırken kapatılmış olabilir.
                    if (_isClosing) return;

                    await _webView.EnsureCoreWebView2Async(env);
                    if (_isClosing || _webView.CoreWebView2 == null) return;

                    _developerBridge.Attach(_webView.CoreWebView2, Dispatcher);
                    if (!_webViewZoomHooked)
                    {
                        _webView.ZoomFactorChanged += OnWebViewZoomFactorChanged;
                        _webViewZoomHooked = true;
                    }
                    // Uygulama kendi F1/F11 ve benzeri kısayollarını yönetir.
                    // Chromium'un yerleşik yardım/accelerator davranışlarının tuşu yutmasını engelle.
                    try { _webView.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = false; } catch { }

                    // Security hardening: this WebView is an application surface, not a general browser.
                    // Keep credentials/autofill and browser UI out of the trusted local app surface.
                    try { _webView.CoreWebView2.Settings.IsPasswordAutosaveEnabled = false; } catch { }
                    try { _webView.CoreWebView2.Settings.IsGeneralAutofillEnabled = false; } catch { }
                    try { _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false; } catch { }
                    try { _webView.CoreWebView2.Settings.IsStatusBarEnabled = false; } catch { }
#if !DEBUG
                    try { _webView.CoreWebView2.Settings.AreDevToolsEnabled = false; } catch { }
#endif

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
                    _webView.CoreWebView2.NavigationStarting += OnNavigationStarting;
                    _webView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
                    _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                    _webView.CoreWebView2.ProcessFailed += OnProcessFailed;

                    // R8: Release derlemelerinde de yalnız log amaçlı DevTools Protocol
                    // console/exception dinleyicilerini bağla. DevTools arayüzü yine kapalıdır.
                    // Böylece bir .alek bootstrap sırasında siyah ekranda kalırsa gerçek
                    // JavaScript exception'ı alekrythae_webview.log içine düşer.
                    await EnableWebDiagnosticsAsync();
                    if (_isClosing) return;
                    // ========================================

                    if (_isClosing) return;
                    FireUpJs();

                    if (!_isClosing && _isViodCeraApp && _viodCeraBridge == null)
                    {
                        _viodCeraBridge = new ViodCeraBridge(this, _webView, SendResponse, AppendWebLog);
                    }
                }
                catch (ObjectDisposedException) when (_isClosing)
                {
                    // Kapanışla yarışan WebView2 başlatma callback'i. Normal yaşam döngüsü.
                }
                catch (Exception ex)
                {
                    if (_isClosing) return;
                    AppendWebLog("startup failed: " + ex);
                    System.Windows.MessageBox.Show($"Çekirdek Hatası: {ex.Message}");
                }
            };
        }

        private async Task UpdateWebViewPowerStateAsync()
        {
            if (_isClosing || _hostExitRequested) return;
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
                        // ViodCera is a resident translator. Keep Chromium warm while the
                        // host window is hidden so global hotkeys can surface a popup instantly.
                        if (_viodCeraBridge?.KeepWarm == true)
                        {
                            if (_webViewSuspended)
                            {
                                try { _webView.CoreWebView2.Resume(); } catch { }
                                _webViewSuspended = false;
                            }
                            if (_webView.Visibility != Visibility.Hidden)
                                _webView.Visibility = Visibility.Hidden;
                            try { _webView.CoreWebView2.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low; } catch { }
                            continue;
                        }
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
                            "window.__alekHasHardBlockingWindow?.() === true ? false : (typeof window.__alekToggleWorldMap === 'function' ? window.__alekToggleWorldMap() : true);");
                    }
                }
                catch (Exception ex)
                {
                    AppendWebLog("Alt+S Map dispatch: " + ex.Message);
                }
                return;
            }

            if (actualKey == WpfKey.F1 && modifiers == WpfModifierKeys.None)
            {
                // R2: F1 artık Kısayol Rehberi değildir. Core, WebView2'den önce
                // tuşu yakaladığı için Mevcudat komutunu doğrudan ortak navigation hattına yollar.
                e.Handled = true;
                await OpenInventoryAsync();
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
                            "window.__alekHasHardBlockingWindow?.() === true ? false : (window.__alekNavigatePrimarySurface ? window.__alekNavigatePrimarySurface('map') : (window.__alekOpenPrimaryRadial ? window.__alekOpenPrimaryRadial() : (window.__ALEK_PENDING_PRIMARY_RADIAL__ = true)));");
                    }
                }
                catch (Exception ex)
                {
                    AppendWebLog("F2 Map dispatch: " + ex.Message);
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

        private async Task OpenInventoryAsync()
        {
            try
            {
                if (_webView?.CoreWebView2 == null) return;
                await _webView.CoreWebView2.ExecuteScriptAsync(
                    "window.__alekHasHardBlockingWindow?.() === true ? false : (window.__alekNavigatePrimarySurface ? window.__alekNavigatePrimarySurface('inventory') : (window.__alekOpenInventoryDirect ? window.__alekOpenInventoryDirect() : true));");
            }
            catch (Exception ex)
            {
                AppendWebLog("F1 Inventory dispatch: " + ex.Message);
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
            _webLog.Write(message);
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

        private static bool IsTrustedBridgeSource(string? source)
        {
            // FireUpJs uses NavigateToString. WebView2 documents that this document
            // has about:blank as its location/origin. Native APIs must never be
            // callable by a document reached through an external navigation.
            return string.Equals(
                source?.Trim(),
                "about:blank",
                StringComparison.OrdinalIgnoreCase);
        }

        private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
        {
            string uri = e.Uri ?? string.Empty;
            if (string.Equals(uri, "about:blank", StringComparison.OrdinalIgnoreCase))
                return;

            // R9 BLACK-SCREEN FIX:
            // WebView2 WPF'nin NavigateToString uygulaması, bizim oluşturduğumuz HTML'i
            // NavigationStarting içinde data:text/html;charset=utf-8;base64,... olarak
            // gösterebilir. R8 bütün data: top-level navigasyonlarını iptal ettiği için
            // Core kendi ürettiği belgeyi daha JavaScript çalışmadan engelliyordu.
            //
            // Güvenlik için data: genel olarak açılmıyor. Sadece FireUpJs tarafından
            // hemen önce silahlandırılmış, kısa ömürlü ve tek kullanımlık başlangıç
            // navigasyonu kabul edilir. Uygulama sonradan location=data: yaparsa bloklanır.
            if (_allowGeneratedDocumentNavigation
                && DateTime.UtcNow <= _generatedDocumentNavigationDeadlineUtc
                && uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase))
            {
                _allowGeneratedDocumentNavigation = false;
                _generatedDocumentNavigationDeadlineUtc = DateTime.MinValue;
                AppendWebLog("allowed generated document navigation");
                return;
            }

            _allowGeneratedDocumentNavigation = false;
            _generatedDocumentNavigationDeadlineUtc = DateTime.MinValue;

            // The embedded surface is deliberately not a browser. Blocking top-level
            // navigation keeps the native file/database/shell bridge attached only to
            // the locally generated Ałek application document.
            e.Cancel = true;
            AppendWebLog("blocked top-level navigation: " + uri);
        }

        private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // Prevent window.open / target=_blank from creating an untrusted browser
            // surface inside the Core process. External browsing belongs outside Core.
            e.Handled = true;
            AppendWebLog("blocked new-window request: " + (e.Uri ?? string.Empty));
        }

        private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess) return;

            // NavigateToString ilk about:blank navigasyonunu yeni belgeyle değiştirdiğinde
            // WebView2 OperationCanceled raporlayabilir. Bu gerçek bir app hatası değildir.
            if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled)
                return;

            AppendWebLog($"navigation failed: {e.WebErrorStatus}");
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

        private static bool IsViodCeraApplication(string jsPath)
        {
            try
            {
                string fileName = Path.GetFileNameWithoutExtension(jsPath);
                if (string.Equals(fileName, "Alekrythae-ViodCera", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!File.Exists(jsPath)) return false;
                string head = string.Join("\n", File.ReadLines(jsPath).Take(120));
                return Regex.IsMatch(head, "alek\\.app\\.id\\s*=\\s*[\'\"]alek\\.viodcera[\'\"]", RegexOptions.IgnoreCase);
            }
            catch
            {
                return false;
            }
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
        private string? ResolveSafePath(string? relativePath) => _paths.Resolve(relativePath);

        // ============================================================
        // C# → JS RESPONSE GÖNDERİCİ
        // ============================================================
        private void SendResponse(string requestId, object result)
        {
            try
            {
                if (_isClosing || _webView?.CoreWebView2 == null)
                    return;

                string json = JsonSerializer.Serialize(result);
                string idEscaped = JsonSerializer.Serialize(requestId);
                string script = $"window.__alekDispatch({idEscaped},{json})";
                _ = _webView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                AppendWebLog("bridge response failed: " + ex);
            }
        }

        // ============================================================
        // HOST KAPANIŞI
        // app.exit, Promise tabanlı veya id'siz legacy mesaj olarak gelebilir.
        // Kapanış WebView/renderer callback'ine bırakılmaz; host doğrudan pencereyi kapatır.
        // ============================================================
        private void HandleHostExitRequest(string? requestId)
        {
            if (_hostExitRequested) return;
            _hostExitRequested = true;
            if (requestId != null) SendResponse(requestId, new { ok = true, closing = true });
            AppendWebLog("app.exit: draining accepted requests before closing");
            Close();
        }

        // ============================================================
        // JS -> C# KÖPRÜSÜ
        // Eski mesajlar (id'siz): { "op":"save", "data":"..." } → geriye uyumlu
        // Yeni mesajlar (id'li):  { "op":"fs.readText", "id":"req_1", "payload":{...} }
        // ============================================================
        // ============================================================
        // API İSTEK İŞLEYİCİ
        // ============================================================
        private void HandleApiRequest(string op, string reqId, JsonElement payload)
        {
            try
            {
                if (_viodCeraBridge != null && _viodCeraBridge.TryHandleApi(op, reqId, payload))
                    return;

                if (TryHandleSafeAiApi(op, reqId, payload))
                    return;

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
                    mode = "google_chrome_app_dock",
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
            _hasSafeAiCssBounds = true;
            ApplySafeAiBoundsToDock();
        }

        private void ApplySafeAiBoundsToDock()
        {
            if (!_hasSafeAiCssBounds)
                return;

            // JavaScript getBoundingClientRect() CSS zoomu ve son yerleşimi
            // zaten hesaba katar. WebView ZoomFactor ayrıca 1.0'a sabitlenir;
            // burada ikinci kez çarpmak F3 Chrome yüzeyini daraltıp kaydırıyordu.
            _edgeChatGptDock.ApplyBounds(
                _safeAiCssBounds.Left,
                _safeAiCssBounds.Top,
                _safeAiCssBounds.Width,
                _safeAiCssBounds.Height);
        }

        private async void OnWebViewZoomFactorChanged(object? sender, EventArgs e)
        {
            if (_webView is not null && Math.Abs(_webView.ZoomFactor - 1.0) > 0.0001)
            {
                _webView.ZoomFactor = 1.0;
                return;
            }
            int generation = ++_safeAiZoomSyncGeneration;
            ApplySafeAiBoundsToDock();

            try
            {
                // Chromium yerleşimi birkaç karede tamamlayabilir. Sayfadan
                // taze görsel rect iste ve aynı ham CSS sınırını pekiştir.
                await Task.Delay(40);
                if (generation != _safeAiZoomSyncGeneration)
                    return;

                ApplySafeAiBoundsToDock();
                if (_webView?.CoreWebView2 != null)
                {
                    await _webView.CoreWebView2.ExecuteScriptAsync(
                        "requestAnimationFrame(()=>requestAnimationFrame(()=>window.__alekSyncSafeAiBounds?.()));");
                }

                await Task.Delay(120);
                if (generation == _safeAiZoomSyncGeneration)
                    ApplySafeAiBoundsToDock();
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
            System.Threading.Interlocked.Increment(ref _documentGeneration);
            _edgeChatGptDock.Hide();
            string jsContent = File.ReadAllText(_jsPath);
            string alekFolder = Path.GetDirectoryName(Path.GetFullPath(_jsPath)) ?? "";
            AppendWebLog($"boot: {Path.GetFileName(_jsPath)} / js={jsContent.Length} chars");

            // Manifest'i dosyadan okuyup doğrudan aynı belgeye enjekte et. NavigateToString
            // ile açılan sayfanın opaque origin'i bazı WebView2 sürümlerinde virtual-host
            // fetch'ini CORS nedeniyle reddedebiliyor. Bu köprü, Alekrythae.App/manifest.json'u
            // gerçek kaynak yapar; .alek içindeki gömülü manifest yalnız güvenli fallback'tir.
            string externalManifestJs = "null";
            try
            {
                string manifestPath = Path.Combine(_rootFolder, "Alekrythae.App", "manifest.json");
                if (File.Exists(manifestPath))
                {
                    string rawManifest = File.ReadAllText(manifestPath);
                    using JsonDocument parsedManifest = JsonDocument.Parse(rawManifest);
                    if (parsedManifest.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        externalManifestJs = JsonSerializer.Serialize(parsedManifest.RootElement);
                        externalManifestJs = Regex.Replace(externalManifestJs, "</script", "<\\/script", RegexOptions.IgnoreCase);
                    }
                }
            }
            catch
            {
                externalManifestJs = "null";
            }

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
                window.__ALEK_EXTERNAL_MANIFEST__ = {externalManifestJs};
                window.__alekAssets = {{
                    moon: ""{EscapeJsString(moonDataUrl)}"",
                    audio: {audioJs},
                    audioName: {audioNameJs},
                    slides: {slidesArrayJs},
                    slideCount: {slideUrls.Count}
                }};
            ";

            string injector = BridgeScript.Build(memoryJson);

            string safeJsContent = Regex.Replace(jsContent, "</script", "<\\/script", RegexOptions.IgnoreCase);
            string html = $"<!doctype html><html><head><meta charset='utf-8'></head><body style='background:transparent; color:white; font-family:sans-serif; margin:0; padding:0; width:100vw; height:100vh; overflow:hidden;'><script>{injector}</script><script>{assetsInjector}</script><script type='module'>{safeJsContent}</script></body></html>";
            AppendWebLog($"boot html: {html.Length} chars");

            // R9: OnNavigationStarting normalde data: top-level navigasyonlarını güvenlik
            // amacıyla engeller. NavigateToString'ın kendi dahili data: navigasyonuna yalnız
            // bu çağrı için izin ver. Bayrak ilk data:text/html olayında tüketilir.
            _allowGeneratedDocumentNavigation = true;
            _generatedDocumentNavigationDeadlineUtc = DateTime.UtcNow.AddSeconds(10);
            try
            {
                _webView.NavigateToString(html);
            }
            catch
            {
                _allowGeneratedDocumentNavigation = false;
                _generatedDocumentNavigationDeadlineUtc = DateTime.MinValue;
                throw;
            }
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


        internal bool IsViodCeraApp => _isViodCeraApp;

        internal void ShowViodCeraMain()
        {
            if (_viodCeraBridge != null) _viodCeraBridge.ShowMainWindow();
            else
            {
                Show();
                Activate();
                Focus();
            }
        }

        internal void ForceCloseViodCera()
        {
            try { _viodCeraBridge?.Dispose(); } catch { }
            _viodCeraBridge = null;
            try { Close(); } catch { }
        }

        protected override void OnClosed(EventArgs e)
        {
            _isClosing = true;
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
            try { _webView.CoreWebView2.NavigationStarting -= OnNavigationStarting; } catch { }
            try { _webView.CoreWebView2.NewWindowRequested -= OnNewWindowRequested; } catch { }
            try { _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted; } catch { }
            try { _webView.CoreWebView2.ProcessFailed -= OnProcessFailed; } catch { }
            try { if (_consoleReceiver != null) _consoleReceiver.DevToolsProtocolEventReceived -= OnConsoleApiCalled; } catch { }
            try { if (_exceptionReceiver != null) _exceptionReceiver.DevToolsProtocolEventReceived -= OnRuntimeExceptionThrown; } catch { }
            try { _viodCeraBridge?.Dispose(); } catch { }
            _viodCeraBridge = null;
            try { _developerBridge.Dispose(); } catch { }
            try { _edgeChatGptDock.Dispose(); } catch { }
            _webView.Dispose();
            base.OnClosed(e);
        }
    }
}
