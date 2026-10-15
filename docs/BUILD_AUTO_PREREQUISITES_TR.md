# Ałek’ryŧhæ Core v2.1.0 | Otomatik bağımlılık denetimi

Bu belgede yalnızca **kaynak derlemesi** ve **WebView2 Runtime** ön koşulları anlatılır.

## Kaynak kodunu derleyen kişi: `BUILD_CORE.cmd`

1. **.NET 10 SDK:** PATH, standart Windows yolları ve önceki kullanıcı kurulumları taranır. Yüklüyse indirilmez. Eksikse Microsoft’un resmî .NET 10 sürüm meta verisinden Windows x64 SDK ZIP’i seçilir, yayınlanan **SHA-512** özeti doğrulanır ve `%LOCALAPPDATA%\Alekrythae\DotNetSdk\` altına kurulur.
2. **Microsoft Edge WebView2 Evergreen Runtime:** Yüklü sürüm `HKCU` ve `HKLM` WebView2 kayıt anahtarlarında denetlenir. Kuruluysa indirme ve yeniden kurulum yapılmaz. Eksikse Microsoft’un resmî Evergreen Bootstrapper bağlantısından yükleyici indirilir. Çalıştırmadan önce **geçerli Authenticode imzası ve Microsoft Corporation imzalayanı** kontrol edilir. Sonrasında `/silent /install` çalıştırılır ve WebView2 sürüm kaydı yeniden doğrulanır.
3. Bağımlılıklar hazırsa mevcut uyumluluk testleri çalıştırılır; başarılıysa self-contained Windows x64 Release üretilir.
4. İndirme, imza, kurulum, test veya derleme başarısız olursa işlem hata vererek durur. Şüpheli bir dosya **çalıştırılmaz**.

**Kaynak:** `tools/Ensure-DotNet10.ps1`, `tools/Ensure-WebView2.ps1`, `tools/Build-Core.ps1`.

## Son kullanıcı: derlenmiş Core

Self-contained Windows Release için **.NET SDK kurulumu gerekmez**. `Alekrythae Core.exe`, geçerli `.alek` dosyasını açarken WebView2 Runtime’ı kontrol eder; eksikse paketteki `Ensure-WebView2.ps1` aracılığıyla aynı Microsoft imzalı yükleyiciyi otomatik kurmaya çalışır. Başarısız olursa sorun mesajı gösterir ve boş WebView2 penceresi açmaz. Sadece EXE dosyasını argümansız çalıştırmak dosya ilişkilendirmesi yapar; WebView2 kurulumunu başlatmaz.

**Önemli:** Dağıtım klasöründe `Ensure-WebView2.ps1` dosyası da bulunmalıdır. Yalnızca EXE’yi tek başına kopyalamayın. `release/` içerisindeki **tam klasörü veya ZIP’i** dağıtın.

Microsoft’a ait indirme bağlantısı: `https://go.microsoft.com/fwlink/p/?LinkId=2124703`.
Resmî manuel çözüm: https://developer.microsoft.com/en-us/microsoft-edge/webview2/.
Microsoft dağıtım yönergesi: https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution.

## Sınırlamalar ve güvenlik notları

- WebView2 ilk kurulumunda **internet gereklidir**; çevrimdışı kurulum için Microsoft’un resmî Standalone Installer’ı ayrı sağlanmalıdır.
- Yönetici yetkisi **normalde gerekmez**; işletim sistemi/kurumsal ilkeler yüklemeyi engellerse sessizce aşılmaz.
- WebView2 bootstrapper resmî Microsoft dijital imzasıyla denetlenir; .NET SDK ise Microsoft’un sürüm meta verisindeki SHA-512 değeriyle doğrulanır. Bunlar farklı doğrulama yöntemleridir.
- Program yayımlanmadan önce Windows 10/11 üzerinde hem **WebView2 kurulu** hem **WebView2 eksik** temiz test ortamlarında derleme, açılış ve yeniden başlatma doğrulanmalıdır.
- Bu değişiklikler kodda uygulanmıştır, ancak teslim edildiği Linux çalışma ortamında gerçek Windows kurulumunun başarıyla çalıştığı **henüz doğrulanmamıştır**.
- `.NET 10` kullanıcı-local kurulumu sistem PATH ayarlarını kalıcı değiştirmez. Aynı klasör altında yayınlanan Release zaten self-contained’dir.
