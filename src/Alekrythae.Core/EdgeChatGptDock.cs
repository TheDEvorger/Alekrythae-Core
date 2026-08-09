using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AlekrythaeCore
{
    /// <summary>
    /// Gerçek Microsoft Edge'in uygulama modu penceresini Meggy'nin AI çalışma
    /// alanına görsel olarak kenetler. Edge ayrı bir tarayıcı işlemi ve kendi
    /// kullanıcı profili olarak kalır. Bu sınıf sayfa içeriğine, DOM'a, ağa,
    /// çerezlere, şifrelere veya oturum anahtarlarına erişmez.
    /// </summary>
    public sealed class EdgeChatGptDock : IDisposable
    {
        public const string HomeUrl = "https://chatgpt.com/";
        private const double EdgeAppTitleBarCropDip = 38;

        private readonly Window _owner;
        private readonly object _launchLock = new();
        private Rect _bounds = new(0, 116, 900, 600);
        private Task<IntPtr>? _launchTask;
        private IntPtr _edgeWindow;
        private IntPtr _originalOwner;
        private long _originalStyle;
        private long _originalExStyle;
        private bool _dockStyleApplied;
        private bool _requestedVisible;
        private bool _syncQueued;
        private bool _disposed;
        private int _openGeneration;

        public EdgeChatGptDock(Window owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _owner.LocationChanged += OnOwnerGeometryChanged;
            _owner.SizeChanged += OnOwnerGeometryChanged;
            _owner.StateChanged += OnOwnerStateChanged;
            _owner.IsVisibleChanged += OnOwnerVisibilityChanged;
        }

        public void ApplyBounds(double left, double top, double width, double height)
        {
            if (_disposed)
                return;

            double safeLeft = Math.Ceiling(Math.Max(0, left));
            double safeTop = Math.Ceiling(Math.Max(0, top));
            double safeWidth = Math.Floor(Math.Max(360, width));
            double safeHeight = Math.Floor(Math.Max(260, height));

            if (double.IsFinite(_owner.ActualWidth) && _owner.ActualWidth > 0)
            {
                safeLeft = Math.Min(safeLeft, Math.Max(0, _owner.ActualWidth - 1));
                safeWidth = Math.Min(
                    safeWidth,
                    Math.Max(1, Math.Floor(_owner.ActualWidth - safeLeft - 2)));
            }

            if (double.IsFinite(_owner.ActualHeight) && _owner.ActualHeight > 0)
            {
                safeTop = Math.Min(safeTop, Math.Max(0, _owner.ActualHeight - 1));
                safeHeight = Math.Min(
                    safeHeight,
                    Math.Max(1, Math.Floor(_owner.ActualHeight - safeTop - 2)));
            }

            _bounds = new Rect(safeLeft, safeTop, safeWidth, safeHeight);
            QueueBoundsSync();
        }

        public async Task OpenAsync()
        {
            ThrowIfDisposed();
            _requestedVisible = true;
            int openGeneration = ++_openGeneration;

            IntPtr edgeWindow = await EnsureEdgeWindowAsync();
            if (_disposed)
            {
                CloseWindowWithoutKillingEdge(edgeWindow);
                return;
            }

            _edgeWindow = edgeWindow;
            // Yeni veya daha önce gizlenmiş Edge penceresinin eski ekran
            // karesini göstermeden önce stilini, bölgesini ve konumunu baştan
            // uygula. Özellikle uygulamanın ikinci açılışında eski kenetleme
            // sınırlarının bir kare görünmesini engeller.
            ShowWindow(_edgeWindow, ShowWindowCommand.Hide);
            SetWindowRgn(_edgeWindow, IntPtr.Zero, false);
            ApplyDockStyle();
            SyncWindowBounds();

            if (_requestedVisible && CanShowDock())
            {
                ShowWindow(_edgeWindow, ShowWindowCommand.Show);
                SetForegroundWindow(_edgeWindow);
                _ = ReinforceDockAfterLaunchAsync(openGeneration);
            }
            else
            {
                ShowWindow(_edgeWindow, ShowWindowCommand.Hide);
            }
        }

        public void Hide()
        {
            _requestedVisible = false;
            _openGeneration++;
            if (IsLiveEdgeWindow())
                ShowWindow(_edgeWindow, ShowWindowCommand.Hide);
        }

        private async Task<IntPtr> EnsureEdgeWindowAsync()
        {
            if (IsLiveEdgeWindow())
                return _edgeWindow;

            _edgeWindow = IntPtr.Zero;
            _dockStyleApplied = false;
            _originalOwner = IntPtr.Zero;
            _originalStyle = 0;
            _originalExStyle = 0;

            Task<IntPtr> task;
            lock (_launchLock)
            {
                if (_launchTask == null || _launchTask.IsCompleted)
                    _launchTask = LaunchEdgeAppWindowAsync();

                task = _launchTask;
            }

            try
            {
                return await task;
            }
            finally
            {
                lock (_launchLock)
                {
                    if (ReferenceEquals(_launchTask, task) && task.IsCompleted)
                        _launchTask = null;
                }
            }
        }

        private static async Task<IntPtr> LaunchEdgeAppWindowAsync()
        {
            string edgePath = FindMicrosoftEdge();
            HashSet<IntPtr> existingWindows = SnapshotEdgeWindows();

            var startInfo = new ProcessStartInfo
            {
                FileName = edgePath,
                Arguments =
                    "--app=\"" + HomeUrl + "\" " +
                    "--new-window --no-first-run --no-default-browser-check",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(edgePath) ?? ""
            };

            Process.Start(startInfo);

            IntPtr fallback = IntPtr.Zero;
            for (int attempt = 0; attempt < 100; attempt++)
            {
                await Task.Delay(150);
                foreach (IntPtr window in SnapshotEdgeWindows())
                {
                    if (existingWindows.Contains(window))
                        continue;

                    string title = ReadWindowTitle(window);
                    if (title.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase))
                    {
                        await Task.Delay(250);
                        if (IsWindow(window))
                            return window;
                    }

                    if (IsWindowVisible(window) &&
                        (fallback == IntPtr.Zero || !string.IsNullOrWhiteSpace(title)))
                    {
                        fallback = window;
                    }
                }

                // Yavaş ağda başlık geç gelebilir. Yalnız yeni oluşturulan ve
                // görünür olan Edge penceresini birkaç saniye kararlı kaldıktan
                // sonra güvenli geri dönüş olarak kabul et.
                if (attempt >= 20 &&
                    fallback != IntPtr.Zero &&
                    IsWindow(fallback) &&
                    IsWindowVisible(fallback))
                {
                    await Task.Delay(250);
                    if (IsWindow(fallback))
                        return fallback;
                }
            }

            throw new InvalidOperationException(
                "Microsoft Edge açıldı fakat ChatGPT uygulama penceresi bulunamadı. " +
                "Açık Edge pencerelerini kapatıp yeniden deneyin.");
        }

        private void ApplyDockStyle()
        {
            if (!IsLiveEdgeWindow())
                return;

            IntPtr ownerHandle = new WindowInteropHelper(_owner).Handle;
            if (ownerHandle == IntPtr.Zero)
                throw new InvalidOperationException("Meggy pencere tutamacı henüz hazır değil.");

            bool firstApplication = !_dockStyleApplied;
            if (!_dockStyleApplied)
            {
                _originalStyle = GetWindowLongPtr(_edgeWindow, WindowLongIndex.Style).ToInt64();
                _originalExStyle = GetWindowLongPtr(_edgeWindow, WindowLongIndex.ExStyle).ToInt64();
                _originalOwner = GetWindowLongPtr(_edgeWindow, WindowLongIndex.ParentOrOwner);
                _dockStyleApplied = true;
            }

            try
            {
                // Edge gezinme veya profil başlangıcı sırasında pencere stilini
                // yeniden yazabildiği için değerleri her eşitlemede mevcut
                // pencereden okuyup başlıksız hâli tekrar uygula.
                long currentStyle =
                    GetWindowLongPtr(_edgeWindow, WindowLongIndex.Style).ToInt64();
                long style = currentStyle;
                style &= ~(WindowStyleBits.Caption |
                           WindowStyleBits.ThickFrame |
                           WindowStyleBits.MinimizeBox |
                           WindowStyleBits.MaximizeBox |
                           WindowStyleBits.SystemMenu);

                long currentExStyle =
                    GetWindowLongPtr(_edgeWindow, WindowLongIndex.ExStyle).ToInt64();
                long exStyle = currentExStyle;
                exStyle &= ~WindowExStyleBits.AppWindow;
                exStyle |= WindowExStyleBits.ToolWindow;

                bool frameChanged = style != currentStyle || exStyle != currentExStyle;
                if (firstApplication)
                    ShowWindow(_edgeWindow, ShowWindowCommand.Restore);
                if (style != currentStyle)
                    SetWindowLongPtr(_edgeWindow, WindowLongIndex.Style, new IntPtr(style));
                if (exStyle != currentExStyle)
                    SetWindowLongPtr(_edgeWindow, WindowLongIndex.ExStyle, new IntPtr(exStyle));

                // SetParent kullanılmaz. Edge gerçek bir üst seviye tarayıcı
                // penceresi olarak kalır; yalnız Meggy onun sahibi olur. Böylece
                // Google/OpenAI onu geliştirici kontrollü gömülü WebView olarak
                // görmez.
                if (GetWindowLongPtr(
                        _edgeWindow,
                        WindowLongIndex.ParentOrOwner) != ownerHandle)
                {
                    frameChanged = true;
                    SetWindowLongPtr(
                        _edgeWindow,
                        WindowLongIndex.ParentOrOwner,
                        ownerHandle);
                }
                if (GetWindowLongPtr(
                        _edgeWindow,
                        WindowLongIndex.ParentOrOwner) != ownerHandle)
                {
                    throw new InvalidOperationException(
                        "Microsoft Edge penceresi Meggy'ye güvenli biçimde kenetlenemedi.");
                }

                if (frameChanged)
                {
                    SetWindowPos(
                        _edgeWindow,
                        IntPtr.Zero,
                        0,
                        0,
                        0,
                        0,
                        SetWindowPositionFlags.NoMove |
                        SetWindowPositionFlags.NoSize |
                        SetWindowPositionFlags.NoZOrder |
                        SetWindowPositionFlags.NoActivate |
                        SetWindowPositionFlags.FrameChanged);
                }
            }
            catch
            {
                RestoreWindowStyle();
                throw;
            }
        }

        private void SyncWindowBounds()
        {
            _syncQueued = false;
            if (_disposed || !_requestedVisible || !IsLiveEdgeWindow() || !CanShowDock())
                return;

            try
            {
                ApplyDockStyle();

                Point screenTopLeft = _owner.PointToScreen(
                    new Point(_bounds.Left, _bounds.Top));
                DpiScale dpi = VisualTreeHelper.GetDpi(_owner);

                int x = (int)Math.Round(screenTopLeft.X);
                int y = (int)Math.Round(screenTopLeft.Y);
                int width = Math.Max(1, (int)Math.Floor(_bounds.Width * dpi.DpiScaleX));
                int height = Math.Max(1, (int)Math.Floor(_bounds.Height * dpi.DpiScaleY));
                int titleBarCrop = Math.Max(
                    1,
                    (int)Math.Round(EdgeAppTitleBarCropDip * dpi.DpiScaleY));

                // Edge --app penceresi klasik WS_CAPTION kaldırıldıktan sonra
                // bile kendi istemci yüzeyinde bir başlık şeridi çizebilir.
                // Pencereyi şerit kadar yukarı taşıyıp görünür bölgeyi aşağıdan
                // başlatarak bu şeridi tamamen dışarıda bırak. Sayfa içeriği
                // Meggy'nin AI alanının tam üst kenarında başlar; alt sınır
                // değişmez.
                int nativeY = y - titleBarCrop;
                int nativeHeight = height + titleBarCrop;

                SetWindowPos(
                    _edgeWindow,
                    IntPtr.Zero,
                    x,
                    nativeY,
                    width,
                    nativeHeight,
                    SetWindowPositionFlags.NoActivate |
                    SetWindowPositionFlags.ShowWindow);

                int radius = Math.Max(
                    1,
                    (int)Math.Round(18 * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY)));
                IntPtr region = CreateRoundRectRgn(
                    0,
                    titleBarCrop,
                    width + 1,
                    nativeHeight + 1,
                    radius * 2,
                    radius * 2);
                if (region != IntPtr.Zero && SetWindowRgn(_edgeWindow, region, true) == 0)
                    DeleteObject(region);
            }
            catch
            {
                // DPI veya ekran geçişinin tek bir ara karesi kenetlemeyi bozmasın.
                QueueBoundsSync();
            }
        }

        private async Task ReinforceDockAfterLaunchAsync(int openGeneration)
        {
            // Chromium pencere stillerini açılıştan sonraki ilk birkaç karede
            // yeniden kurabildiğinden başlıksız görünümü kısa aralıklarla
            // pekiştir. Bu işlem yalnız pencere geometrisine dokunur; web sayfası
            // veya tarayıcı verileri okunmaz.
            int[] delays = { 80, 240, 700 };
            foreach (int delay in delays)
            {
                await Task.Delay(delay);
                if (_disposed ||
                    !_requestedVisible ||
                    openGeneration != _openGeneration)
                {
                    return;
                }

                await _owner.Dispatcher.InvokeAsync(
                    () =>
                    {
                        if (!_disposed &&
                            _requestedVisible &&
                            openGeneration == _openGeneration)
                        {
                            SyncWindowBounds();
                        }
                    },
                    DispatcherPriority.Background);
            }
        }

        private bool CanShowDock()
        {
            return _owner.IsVisible &&
                   _owner.WindowState != WindowState.Minimized &&
                   new WindowInteropHelper(_owner).Handle != IntPtr.Zero;
        }

        private void QueueBoundsSync()
        {
            if (_disposed || !_requestedVisible || _syncQueued)
                return;

            _syncQueued = true;
            _owner.Dispatcher.BeginInvoke(
                new Action(SyncWindowBounds),
                DispatcherPriority.Background);
        }

        private void OnOwnerGeometryChanged(object? sender, EventArgs e)
        {
            QueueBoundsSync();
        }

        private void OnOwnerStateChanged(object? sender, EventArgs e)
        {
            if (!IsLiveEdgeWindow())
                return;

            if (!CanShowDock() || !_requestedVisible)
                ShowWindow(_edgeWindow, ShowWindowCommand.Hide);
            else
                QueueBoundsSync();
        }

        private void OnOwnerVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            OnOwnerStateChanged(sender, EventArgs.Empty);
        }

        private bool IsLiveEdgeWindow()
        {
            return _edgeWindow != IntPtr.Zero &&
                   IsWindow(_edgeWindow) &&
                   IsMicrosoftEdgeWindow(_edgeWindow);
        }

        private static HashSet<IntPtr> SnapshotEdgeWindows()
        {
            var result = new HashSet<IntPtr>();
            EnumWindows((window, _) =>
            {
                if (IsMicrosoftEdgeWindow(window))
                    result.Add(window);
                return true;
            }, IntPtr.Zero);
            return result;
        }

        private static bool IsMicrosoftEdgeWindow(IntPtr window)
        {
            if (window == IntPtr.Zero ||
                !IsWindow(window) ||
                GetAncestor(window, 2) != window)
            {
                return false;
            }

            string className = ReadWindowClass(window);
            if (!className.StartsWith("Chrome_WidgetWin_", StringComparison.Ordinal))
                return false;

            GetWindowThreadProcessId(window, out uint processId);
            if (processId == 0)
                return false;

            try
            {
                using Process process = Process.GetProcessById((int)processId);
                return string.Equals(
                    process.ProcessName,
                    "msedge",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string FindMicrosoftEdge()
        {
            var candidates = new List<string>();
            AddCandidate(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            AddCandidate(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            AddCandidate(candidates, Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

            object? appPath = Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\msedge.exe",
                "",
                null);
            if (appPath is string registryPath && !string.IsNullOrWhiteSpace(registryPath))
                candidates.Add(registryPath);

            foreach (string candidate in candidates)
            {
                try
                {
                    if (!File.Exists(candidate) ||
                        !string.Equals(
                            Path.GetFileName(candidate),
                            "msedge.exe",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    FileVersionInfo version = FileVersionInfo.GetVersionInfo(candidate);
                    if (version.CompanyName?.Contains(
                            "Microsoft",
                            StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // Sonraki bilinen Edge kurulum konumunu dene.
                }
            }

            throw new FileNotFoundException(
                "Microsoft Edge bulunamadı. Windows'taki resmî Microsoft Edge kurulumunu onarın.");
        }

        private static void AddCandidate(List<string> candidates, string root)
        {
            if (!string.IsNullOrWhiteSpace(root))
                candidates.Add(Path.Combine(root, "Microsoft", "Edge", "Application", "msedge.exe"));
        }

        private static string ReadWindowClass(IntPtr window)
        {
            var value = new StringBuilder(256);
            GetClassName(window, value, value.Capacity);
            return value.ToString();
        }

        private static string ReadWindowTitle(IntPtr window)
        {
            int length = Math.Max(0, GetWindowTextLength(window));
            var value = new StringBuilder(length + 1);
            GetWindowText(window, value, value.Capacity);
            return value.ToString();
        }

        private void RestoreWindowStyle()
        {
            if (!_dockStyleApplied || !IsWindow(_edgeWindow))
                return;

            SetWindowRgn(_edgeWindow, IntPtr.Zero, true);
            SetWindowLongPtr(
                _edgeWindow,
                WindowLongIndex.ParentOrOwner,
                _originalOwner);
            SetWindowLongPtr(
                _edgeWindow,
                WindowLongIndex.Style,
                new IntPtr(_originalStyle));
            SetWindowLongPtr(
                _edgeWindow,
                WindowLongIndex.ExStyle,
                new IntPtr(_originalExStyle));
            SetWindowPos(
                _edgeWindow,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                SetWindowPositionFlags.NoMove |
                SetWindowPositionFlags.NoSize |
                SetWindowPositionFlags.NoZOrder |
                SetWindowPositionFlags.NoActivate |
                SetWindowPositionFlags.FrameChanged);
            _dockStyleApplied = false;
        }

        private void CloseWindowWithoutKillingEdge(IntPtr window)
        {
            if (window == IntPtr.Zero || !IsWindow(window))
                return;

            if (window == _edgeWindow)
                RestoreWindowStyle();
            PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EdgeChatGptDock));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _requestedVisible = false;
            _openGeneration++;
            _owner.LocationChanged -= OnOwnerGeometryChanged;
            _owner.SizeChanged -= OnOwnerGeometryChanged;
            _owner.StateChanged -= OnOwnerStateChanged;
            _owner.IsVisibleChanged -= OnOwnerVisibilityChanged;

            if (IsWindow(_edgeWindow))
            {
                ShowWindow(_edgeWindow, ShowWindowCommand.Hide);
                RestoreWindowStyle();
                PostMessage(_edgeWindow, 0x0010, IntPtr.Zero, IntPtr.Zero);
            }

            _edgeWindow = IntPtr.Zero;
        }

        private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

        private enum WindowLongIndex
        {
            Style = -16,
            ExStyle = -20,
            ParentOrOwner = -8
        }

        private enum ShowWindowCommand
        {
            Hide = 0,
            Show = 5,
            Restore = 9
        }

        [Flags]
        private enum SetWindowPositionFlags : uint
        {
            NoSize = 0x0001,
            NoMove = 0x0002,
            NoZOrder = 0x0004,
            NoActivate = 0x0010,
            FrameChanged = 0x0020,
            ShowWindow = 0x0040
        }

        private static class WindowStyleBits
        {
            public const long Caption = 0x00C00000L;
            public const long ThickFrame = 0x00040000L;
            public const long MinimizeBox = 0x00020000L;
            public const long MaximizeBox = 0x00010000L;
            public const long SystemMenu = 0x00080000L;
        }

        private static class WindowExStyleBits
        {
            public const long ToolWindow = 0x00000080L;
            public const long AppWindow = 0x00040000L;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool EnumWindows(
            EnumWindowsCallback callback,
            IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindow(IntPtr window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(
            IntPtr window,
            StringBuilder className,
            int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(
            IntPtr window,
            StringBuilder text,
            int maximumCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr window);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(
            IntPtr window,
            out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            SetWindowPositionFlags flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(
            IntPtr window,
            ShowWindowCommand command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowRgn(
            IntPtr window,
            IntPtr region,
            [MarshalAs(UnmanagedType.Bool)] bool redraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(
            int left,
            int top,
            int right,
            int bottom,
            int ellipseWidth,
            int ellipseHeight);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr value);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool PostMessage(
            IntPtr window,
            uint message,
            IntPtr wParam,
            IntPtr lParam);

        private static IntPtr GetWindowLongPtr(IntPtr window, WindowLongIndex index)
        {
            return IntPtr.Size == 8
                ? GetWindowLongPtr64(window, (int)index)
                : new IntPtr(GetWindowLong32(window, (int)index));
        }

        private static IntPtr SetWindowLongPtr(
            IntPtr window,
            WindowLongIndex index,
            IntPtr newValue)
        {
            return IntPtr.Size == 8
                ? SetWindowLongPtr64(window, (int)index, newValue)
                : new IntPtr(SetWindowLong32(window, (int)index, newValue.ToInt32()));
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong32(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(
            IntPtr window,
            int index,
            IntPtr newValue);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
        private static extern int SetWindowLong32(
            IntPtr window,
            int index,
            int newValue);
    }
}
