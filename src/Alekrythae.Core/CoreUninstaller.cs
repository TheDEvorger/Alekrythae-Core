using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace AlekrythaeCore
{
    internal static class CoreUninstaller
    {
        private const string UninstallerFileName = "KALDIR_ALEKRYTHAE_CORE.ps1";
        private const string BundleMarker = ".alekrythae-bundle-root";

        public static void OpenCoreFolder()
        {
            try
            {
                string coreFolder = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
                var startInfo = new ProcessStartInfo("explorer.exe")
                {
                    UseShellExecute = true
                };
                startInfo.ArgumentList.Add(coreFolder);
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Core klasörü açılamadı:\n\n" + ex.Message,
                    "Ałek’ryŧhæ Core",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        public static void BeginUninstall(int activeDimensionCount, Action shutdownCore)
        {
            if (activeDimensionCount > 0)
            {
                MessageBox.Show(
                    $"Aktif {activeDimensionCount} boyut açık. Kaldırmadan önce bütün boyutları kapat.",
                    "Ałek’ryŧhæ Core",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            MessageBoxResult first = MessageBox.Show(
                "Ałek’ryŧhæ Core bilgisayardan kaldırılacak.\n\n" +
                "Dosya ilişkilendirmesi, Core önbelleği, GPU tercihi ve Ałek’ryŧhæ kısayolları temizlenecek.\n\n" +
                "Devam edilsin mi?",
                "Ałek’ryŧhæ Core'u Kaldır",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (first != MessageBoxResult.Yes)
                return;

            MessageBoxResult dataChoice = MessageBox.Show(
                "Paket ve kullanıcı verileri de silinsin mi?\n\n" +
                "EVET: Core + paket + Data, Journey, Media, Raptiye, MapLibrary ve .alek dosyaları dahil tamamını kaldırır.\n\n" +
                "HAYIR: Yalnız Core'u, kayıtları, önbelleği ve kısayolları kaldırır; paket ve macera verilerini korur.\n\n" +
                "İPTAL: Hiçbir şey yapmaz.",
                "Kaldırma Türü",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.Cancel);

            if (dataChoice == MessageBoxResult.Cancel)
                return;

            string mode = dataChoice == MessageBoxResult.Yes
                ? "Everything"
                : "CoreOnly";

            try
            {
                string coreFolder = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string scriptSource = Path.Combine(coreFolder, UninstallerFileName);

                if (!File.Exists(scriptSource))
                {
                    MessageBox.Show(
                        $"Kaldırıcı dosyası bulunamadı:\n{scriptSource}\n\n" +
                        "Core paketini yeniden çıkarıp tekrar deneyin.",
                        "Ałek’ryŧhæ Core",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                string tempScript = Path.Combine(
                    Path.GetTempPath(),
                    $"Alekrythae_Uninstall_{Guid.NewGuid():N}.ps1");
                File.Copy(scriptSource, tempScript, true);

                string bundleRoot = FindBundleRoot(coreFolder) ?? coreFolder;

                var startInfo = new ProcessStartInfo("powershell.exe")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetTempPath()
                };
                startInfo.ArgumentList.Add("-NoLogo");
                startInfo.ArgumentList.Add("-NoProfile");
                startInfo.ArgumentList.Add("-ExecutionPolicy");
                startInfo.ArgumentList.Add("Bypass");
                startInfo.ArgumentList.Add("-File");
                startInfo.ArgumentList.Add(tempScript);
                startInfo.ArgumentList.Add("-InstallRoot");
                startInfo.ArgumentList.Add(coreFolder);
                startInfo.ArgumentList.Add("-BundleRoot");
                startInfo.ArgumentList.Add(bundleRoot);
                startInfo.ArgumentList.Add("-ProcessId");
                startInfo.ArgumentList.Add(Process.GetCurrentProcess().Id.ToString());
                startInfo.ArgumentList.Add("-Mode");
                startInfo.ArgumentList.Add(mode);

                Process? uninstaller = Process.Start(startInfo);
                if (uninstaller == null)
                    throw new InvalidOperationException("Kaldırıcı süreç başlatılamadı.");

                shutdownCore();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Kaldırıcı başlatılamadı:\n\n" + ex.Message,
                    "Ałek’ryŧhæ Core",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private static string? FindBundleRoot(string startFolder)
        {
            DirectoryInfo? current = new DirectoryInfo(startFolder);
            for (int depth = 0; current != null && depth < 10; depth++, current = current.Parent)
            {
                if (File.Exists(Path.Combine(current.FullName, BundleMarker)))
                    return current.FullName;
            }

            return null;
        }
    }
}
