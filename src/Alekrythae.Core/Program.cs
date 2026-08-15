using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using Application = System.Windows.Application;

namespace AlekrythaeCore
{
    public class Program
    {
        private static NotifyIcon? _trayIcon;
        private static Application? _app;
        private const string Extension = ".alek";
        private const string ProgId = "Alekrythae.Nexus.v0";
        // 'in ikinci açılış AI yaşam döngüsü eski Core tepside açık
        // olsa bile güncel süreçte çalışmalıdır. Ayrı pipe/mutex eski Core'a
        // yanlışlıkla yönlendirme yapılmasını engeller.
        private const string PipeName = "AlekrythaePipeLine.AiReopenRevision11";

        private static readonly Dictionary<string, CosmicGate> _activeDimensions =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly Mutex _mutex =
            new(false, "{Alekrythae-Core-Revision-11-Second-Launch-Dock-Recovery-Identity-Key}");

        private static bool _ownsMutex;

        [STAThread]
        public static void Main(string[] args)
        {
            _ownsMutex = _mutex.WaitOne(TimeSpan.Zero, false);

            if (!_ownsMutex)
            {
                if (args.Length > 0)
                {
                    SendFileToMaster(args[0]);
                }
                return;
            }

            try
            {
                GraphicsBridge.ApplyPersistedWindowsPreference();

                _app = new Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };

                // Global hata yakalayıcı — sessiz çökmeleri logla
                string crashPath1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alekrythae_crash.log");
                string crashPath2 = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "") ?? AppDomain.CurrentDomain.BaseDirectory, "alekrythae_crash.log");

                void WriteCrash(string msg)
                {
                    foreach (var p in new[] { crashPath1, crashPath2 })
                    {
                        try { File.AppendAllText(p, msg); return; } catch { }
                    }
                }

                AppDomain.CurrentDomain.UnhandledException += (s, ue) =>
                {
                    WriteCrash($"[{DateTime.Now}] AppDomain.Unhandled:\n{ue.ExceptionObject}\n\n");
                };

                _app.DispatcherUnhandledException += (s, ex) =>
                {
                    WriteCrash($"[{DateTime.Now}] DispatcherUnhandled:\n{ex.Exception}\n\n");
                    ex.Handled = true;
                };

                RegisterExtension();
                RegisterUninstallEntry();

                // Core artık tek başına arka planda beklemez.
                // Bir .alek dosyasıyla başlatılmadıysa kayıt işlemlerini tamamlayıp kapanır.
                if (args.Length == 0)
                {
                    return;
                }

                StartPipeServer();
                SetupTray();

                _app.Dispatcher.BeginInvoke(
                    new Action(() => OpenFromArgument(args[0])));

                _app.Run();
            }
            finally
            {
                try
                {
                    if (_ownsMutex)
                    {
                        _mutex.ReleaseMutex();
                    }
                }
                catch
                {
                    // Uygulama kapanırken mutex zaten bırakılmış olabilir.
                }

                _mutex.Dispose();
            }
        }

        private static void OpenFromArgument(string filePath)
        {
            try
            {
                string fullPath = Path.GetFullPath(filePath);

                if (!File.Exists(fullPath))
                {
                    System.Windows.MessageBox.Show(
                        $"Açılacak dosya bulunamadı:\n{fullPath}",
                        "Ałek’ryŧhæ Core",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ShutdownIfNoDimensions();
                    return;
                }

                if (!string.Equals(Path.GetExtension(fullPath), Extension, StringComparison.OrdinalIgnoreCase))
                {
                    System.Windows.MessageBox.Show(
                        "Yalnızca .alek dosyaları açılabilir.",
                        "Ałek’ryŧhæ Core",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ShutdownIfNoDimensions();
                    return;
                }

                SpawnEmulator(fullPath);
            }
            catch (Exception ex)
            {
                ShowOpenError(ex);
                ShutdownIfNoDimensions();
            }
        }

        private static void OpenAlekDialog()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Ałek’ryŧhæ Boyutu Aç",
                    Filter = "Ałek’ryŧhæ Boyutu (*.alek)|*.alek",
                    CheckFileExists = true,
                    Multiselect = false
                };

                if (dialog.ShowDialog() == true)
                {
                    SpawnEmulator(dialog.FileName);
                }
            }
            catch (Exception ex)
            {
                ShowOpenError(ex);
            }
        }

        private static void StartPipeServer()
        {
            Task.Run(async () =>
            {
                while (_app != null)
                {
                    try
                    {
                        using var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            1,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        await server.WaitForConnectionAsync();

                        using var reader = new StreamReader(server);
                        string? filePath = await reader.ReadLineAsync();

                        if (!string.IsNullOrWhiteSpace(filePath))
                        {
                            _app?.Dispatcher.BeginInvoke(
                                new Action(() => OpenFromArgument(filePath)));
                        }
                    }
                    catch
                    {
                        // Tepsi motoru kapanana kadar yeni bağlantı beklemeye devam et.
                    }
                }
            });
        }

        private static void SendFileToMaster(string filePath)
        {
            Exception? lastError = null;

            // Ana süreç pipe sunucusunu henüz oluşturuyorsa dosya kaybolmasın.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(
                        ".",
                        PipeName,
                        PipeDirection.Out,
                        PipeOptions.None);

                    client.Connect(300);

                    using var writer = new StreamWriter(client)
                    {
                        AutoFlush = true
                    };

                    writer.WriteLine(filePath);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    Thread.Sleep(150);
                }
            }

            System.Windows.MessageBox.Show(
                "Ałek’ryŧhæ Core arka planda çalışıyor fakat dosya ona gönderilemedi. " +
                "Sağ alttaki tepsi simgesinden çıkıp yeniden deneyin.\n\n" +
                lastError?.Message,
                "Ałek’ryŧhæ Core",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static void RegisterExtension()
        {
            try
            {
                string openCommand = BuildOpenCommand();
                string icoPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Resources",
                    "CosmicNexus.ico");

                // .alek -> Alekrythae.Nexus.v0
                using (var extensionKey = Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\{Extension}"))
                {
                    extensionKey?.SetValue("", ProgId);

                    // Windows'un "Birlikte aç" listesinde de görünsün.
                    using var openWithKey = extensionKey?.CreateSubKey("OpenWithProgids");
                    openWithKey?.SetValue(ProgId, string.Empty, RegistryValueKind.String);
                }

                // ProgID ve çift tıklama komutu
                using (var progKey = Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\{ProgId}"))
                {
                    progKey?.SetValue("", "Ałek’ryŧhæ Boyut Dosyası");
                    progKey?.SetValue("FriendlyTypeName", "Ałek’ryŧhæ Boyut Dosyası");

                    using (var iconKey = progKey?.CreateSubKey("DefaultIcon"))
                    {
                        iconKey?.SetValue("", $"\"{icoPath}\",0");
                    }

                    using (var shellKey = progKey?.CreateSubKey("shell"))
                    {
                        shellKey?.SetValue("", "open");
                    }

                    using (var commandKey = progKey?.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey?.SetValue("", openCommand);
                    }
                }

                // Uygulamanın "Birlikte aç" penceresinde düzgün görünmesi için.
                string appRegistrationName = "Alekrythae Core.exe";
                using (var appKey = Registry.CurrentUser.CreateSubKey(
                    $@"Software\Classes\Applications\{appRegistrationName}"))
                {
                    appKey?.SetValue("FriendlyAppName", "Ałek’ryŧhæ Core v0.1.2");

                    using (var supportedTypes = appKey?.CreateSubKey("SupportedTypes"))
                    {
                        supportedTypes?.SetValue(Extension, string.Empty);
                    }

                    using (var commandKey = appKey?.CreateSubKey(@"shell\open\command"))
                    {
                        commandKey?.SetValue("", openCommand);
                    }
                }

                // Explorer'ın ilişkilendirme ve ikon önbelleğini yenile.
                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                // Kayıt başarısız olsa bile Core çalışsın; hata Debug çıktısına düşsün.
                System.Diagnostics.Debug.WriteLine(
                    "Dosya ilişkilendirme kaydı başarısız: " + ex.Message);
            }
        }

        private static void RegisterUninstallEntry()
        {
            try
            {
                string baseFolder = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string uninstallCmd = Path.Combine(baseFolder, "KALDIR_ALEKRYTHAE_CORE.cmd");
                if (!File.Exists(uninstallCmd))
                    return;

                string processPath = Environment.ProcessPath ??
                    System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;
                string comSpec = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                string uninstallCommand = $"\"{comSpec}\" /d /c \"\"{uninstallCmd}\"\"";

                using RegistryKey? key = Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AlekrythaeCore");
                key?.SetValue("DisplayName", "Ałek’ryŧhæ Core");
                key?.SetValue("DisplayVersion", "0.1.2");
                key?.SetValue("Publisher", "Ałek’ryŧhæ");
                key?.SetValue("InstallLocation", baseFolder);
                key?.SetValue("UninstallString", uninstallCommand);
                key?.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key?.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key?.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                if (!string.IsNullOrWhiteSpace(processPath))
                    key?.SetValue("DisplayIcon", $"\"{processPath}\",0");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "Kaldırma kaydı oluşturulamadı: " + ex.Message);
            }
        }

        private static string BuildOpenCommand()
        {
            string processPath = Environment.ProcessPath ?? string.Empty;
            string entryAssemblyPath =
                System.Reflection.Assembly.GetEntryAssembly()?.Location ?? string.Empty;

            if (string.IsNullOrWhiteSpace(processPath))
            {
                throw new InvalidOperationException(
                    "Çalışan Core işleminin yolu bulunamadı.");
            }

            string processFileName = Path.GetFileName(processPath);
            bool isDotnetHost =
                string.Equals(processFileName, "dotnet.exe", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(processFileName, "dotnet", StringComparison.OrdinalIgnoreCase);

            // dotnet run altında ProcessPath, Alekrythae Core.exe değil dotnet.exe olabilir.
            // Bu durumda .alek yolu doğrudan dotnet.exe'ye verilirse Windows'ta hiçbir şey
            // açılmaz. Önce Core DLL'ini, sonra %1 ile .alek yolunu gönderiyoruz.
            if (isDotnetHost)
            {
                if (string.IsNullOrWhiteSpace(entryAssemblyPath))
                {
                    throw new InvalidOperationException(
                        "Ałek’ryŧhæ Core DLL yolu bulunamadı.");
                }

                return $"\"{processPath}\" \"{entryAssemblyPath}\" \"%1\"";
            }

            // Publish/release EXE altında doğrudan Core EXE'sini çalıştır.
            return $"\"{processPath}\" \"%1\"";
        }

        private static void SetupTray()
        {
            string trayIcoPath = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources",
                "Adventurer.ico");

            _trayIcon = new NotifyIcon
            {
                Icon = File.Exists(trayIcoPath)
                    ? new System.Drawing.Icon(trayIcoPath)
                    : System.Drawing.SystemIcons.Shield,
                Visible = true,
                Text = "Ałek’ryŧhæ Core v0.1.2"
            };

            var trayMenu = new ContextMenuStrip();

            var openItem = new ToolStripMenuItem("Ałek’ryŧhæ Boyutu Aç...");
            openItem.Click += (_, _) => OpenAlekDialog();

            var openFolderItem = new ToolStripMenuItem("Core Dosya Konumunu Aç");
            openFolderItem.Click += (_, _) => CoreUninstaller.OpenCoreFolder();

            var uninstallItem = new ToolStripMenuItem("Ałek’ryŧhæ Core'u Kaldır...");
            uninstallItem.Click += (_, _) =>
                CoreUninstaller.BeginUninstall(_activeDimensions.Count, ShutdownEngine);

            var exitItem = new ToolStripMenuItem("Core'dan Çık");
            exitItem.Click += (_, _) =>
            {
                if (_activeDimensions.Count > 0)
                {
                    System.Windows.MessageBox.Show(
                        $"Aktif {_activeDimensions.Count} boyut açık. Önce onları kapat.",
                        "Ałek’ryŧhæ Core",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                ShutdownEngine();
            };

            trayMenu.Items.Add(openItem);
            trayMenu.Items.Add(openFolderItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(uninstallItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exitItem);
            _trayIcon.ContextMenuStrip = trayMenu;

            _trayIcon.MouseClick += (_, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    OpenAlekDialog();
                }
            };
        }

        private static void SpawnEmulator(string jsPath)
        {
            try
            {
                string fullPath = Path.GetFullPath(jsPath);

                if (!File.Exists(fullPath))
                {
                    throw new FileNotFoundException(".alek dosyası bulunamadı.", fullPath);
                }

                if (_activeDimensions.TryGetValue(fullPath, out CosmicGate? existing))
                {
                    existing.WindowState = WindowState.Normal;
                    existing.Show();
                    existing.Activate();
                    existing.Focus();
                    return;
                }

                var win = new CosmicGate(fullPath);
                _activeDimensions.Add(fullPath, win);
                win.Closed += (_, _) =>
                {
                    _activeDimensions.Remove(fullPath);
                    ShutdownIfNoDimensions();
                };

                win.Show();
                win.Activate();
                win.Focus();
                win.Topmost = true;
                win.Topmost = false;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "alekrythae_crash.log"), $"[{DateTime.Now}] SpawnEmulator HATA:\n{ex}\n"); } catch { }
                ShowOpenError(ex);
                ShutdownIfNoDimensions();
            }
        }

        private static void ShutdownIfNoDimensions()
        {
            if (_activeDimensions.Count == 0)
            {
                ShutdownEngine();
            }
        }

        private static void ShowOpenError(Exception ex)
        {
            System.Windows.MessageBox.Show(
                "Boyut açılamadı:\n\n" + ex.Message +
                (ex.InnerException != null ? "\n\nİç hata: " + ex.InnerException.Message : string.Empty),
                "Ałek’ryŧhæ Core",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        private static void ShutdownEngine()
        {
            // Normal çıkışta dosya ilişkilendirmesini SİLME.
            // İlişkilendirme yalnızca gerçek bir kaldırıcı tarafından kaldırılmalıdır.
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _app?.Shutdown();
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(
            uint wEventId,
            uint uFlags,
            IntPtr dwItem1,
            IntPtr dwItem2);
    }
}
