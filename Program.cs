using System;
using System.IO;
using System.IO.Pipes;
using System.Windows;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Application = System.Windows.Application;

namespace AlekrythaeCore
{
    public class Program
    {
        private static NotifyIcon? _trayIcon;
        private static Application? _app;
        private const string Extension = ".alek";
        // Windows ikonları yenilesin diye v6 yaptık
        private const string ProgId = "Alekrythae.Nexus.v6";
        private const string PipeName = "AlekrythaePipeLine";

        private static Dictionary<string, CosmicGate> _activeDimensions = new Dictionary<string, CosmicGate>();
        private static Mutex _mutex = new Mutex(true, "{Alekrythae-Core-Unique-Identity-Key}");

        [STAThread]
        public static void Main(string[] args)
        {
            if (!_mutex.WaitOne(TimeSpan.Zero, true))
            {
                if (args.Length > 0) SendFileToMaster(args[0]);
                return;
            }

            _app = new Application();
            _app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            RegisterExtension();
            StartPipeServer();
            SetupTray();

            if (args.Length > 0 && Path.GetExtension(args[0]).ToLower() == Extension)
            {
                SpawnEmulator(args[0]);
            }

            _app.Run();
            _mutex.ReleaseMutex();
        }

        private static void StartPipeServer()
        {
            Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In))
                        {
                            await server.WaitForConnectionAsync();
                            using (var reader = new StreamReader(server))
                            {
                                string? filePath = await reader.ReadLineAsync();
                                if (!string.IsNullOrEmpty(filePath))
                                {
                                    _app?.Dispatcher.Invoke(() => SpawnEmulator(filePath));
                                }
                            }
                        }
                    }
                    catch { }
                }
            });
        }

        private static void SendFileToMaster(string filePath)
        {
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(500);
                    using (var writer = new StreamWriter(client)) { writer.WriteLine(filePath); writer.Flush(); }
                }
            }
            catch { }
        }

        // DOSYA SİMGESİ: CosmicNexus.ico (Meggy - Masaüstü/Klasör sembolü)
        private static void RegisterExtension()
        {
            try
            {
                string exePath = Environment.ProcessPath ?? System.Reflection.Assembly.GetExecutingAssembly().Location;
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "CosmicNexus.ico");

                using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Extension}"))
                {
                    key.SetValue("", ProgId);
                }

                using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
                {
                    key.SetValue("", "Ałek’ryŧhæ Boyut Dosyası");
                    using (var iconKey = key.CreateSubKey("DefaultIcon"))
                    {
                        iconKey.SetValue("", $"{icoPath},0");
                    }
                    using (var shellKey = key.CreateSubKey(@"shell\open\command"))
                    {
                        shellKey.SetValue("", $"\"{exePath}\" \"%1\"");
                    }
                }

                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        // TEPSİ SİMGESİ (Sağ Alt Köşe): Adventurer.ico (Meşaleli Maceracı)
        private static void SetupTray()
        {
            string trayIcoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Adventurer.ico");

            _trayIcon = new NotifyIcon
            {
                Icon = File.Exists(trayIcoPath) ? new System.Drawing.Icon(trayIcoPath) : System.Drawing.SystemIcons.Shield,
                Visible = true,
                Text = "Ałek’ryŧhæ Kontrol Paneli"
            };

            _trayIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    if (_activeDimensions.Count > 0)
                        System.Windows.MessageBox.Show($"Aktif {_activeDimensions.Count} boyut açık! Önce onları kapat kankam.");
                    else
                        ShutdownEngine();
                }
                else if (e.Button == MouseButtons.Left)
                {
                    var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Ałek’ryŧhæ Boyutu (*.alek)|*.alek" };
                    if (dialog.ShowDialog() == true) SpawnEmulator(dialog.FileName);
                }
            };
        }

        private static void SpawnEmulator(string jsPath)
        {
            string fullPath = Path.GetFullPath(jsPath).ToLower();
            if (_activeDimensions.ContainsKey(fullPath))
            {
                _activeDimensions[fullPath].WindowState = WindowState.Normal;
                _activeDimensions[fullPath].Activate();
                return;
            }

            var win = new CosmicGate(jsPath);
            _activeDimensions.Add(fullPath, win);
            win.Closed += (s, e) => _activeDimensions.Remove(fullPath);

            win.Show();
            win.Activate();
            win.Focus();
            win.Topmost = true;
            win.Topmost = false;
        }

        private static void ShutdownEngine()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Extension}", false); } catch { }
            if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
            _app?.Shutdown();
        }

        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
    }
}