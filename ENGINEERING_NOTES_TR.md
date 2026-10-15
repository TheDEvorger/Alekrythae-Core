# Core mühendislik notları — uyumluluk, kayıt güvenliği ve arayüz tepkiselliği

> **Arşiv notu (10 Ekim 2026):** Bu belge, önceki geliştirme döneminde bildirilen test ve derleme gözlemlerini tarihî bilgi olarak saklar. Güncel uygulamanın tek sürüm kimliği **v2.1.0**'dır. Bu notlardaki geçmiş test sonuçları yeni sürümün çalıştığı anlamına gelmez.

25 Eylül 2026 tarihli önceki geliştirme notları. Bu kayıtlar güncel sürüm etiketini belirlemez.

## Değişen davranış

- Dosya/SQLite işlemleri tek tüketicili arka plan kuyruğunda yürür. WPF/WebView2 çağrıları ve dosya seçicileri arayüz iş parçacığında kalır. İçe/dışa aktarmanın paketleme, kopyalama ve veritabanı işleri arka planda yürür.
- Kuyruk pencere başına en fazla 128 bekleyen istek ve tahmini 64 MiB istek verisi kabul eder; taşmada Promise API `core_busy` döndürür. Eski `__alekSave` dönüşü, önceki sürümde olduğu gibi yalnız mesaj gönderimini bildirir; kalıcı kayıt onayı değildir.
- Aynı uygulama kökünden açılan pencereler ortak bir kilitle depolama işlemlerini sıraya alır. Başka uygulama kökleri bağımsız çalışabilir. Kabul edilen işlemler kapanışta tamamlanır; önceki 1,5 saniyelik zorla süreç kapatma yolu kaldırılmıştır. Bekleyen kullanıcı seçicisi kullanıcı yanıtını bekleyebilir.
- Belge değişiminden kalan istekler yeni sayfadaki aynı numaralı Promise'i sonuçlandıramaz. JSON payload'ları kaynak belge kapatılmadan kopyalanır.
- SQLite yollarına da dosya API'siyle ortak kök/junction/symlink/alternatif veri akışı denetimi uygulanır. Veritabanının WAL/SHM/journal yan dosyaları ayrıca denetlenir. Bu uygulama içi yol sınırıdır; yerel bir sürecin eşzamanlı dosya sistemi değiştirmesine karşı işletim sistemi izolasyonu iddiası değildir.
- SQLite uzun Unicode Windows yollarıyla açılır. `win32-longpath` VFS ve genişletilmiş Windows yol öneki birlikte kullanılır; başarısız bağlantılar dispose edilir.
- Atomik yazma geçici dosyayı diske flush edip hedefe taşır. Kilitli hedefte eski dosya korunur ve geçici dosya temizlenir. Güç kesintisi/donanım arızası için mutlak garanti verilmez.
- Tanılama logları arka planda toplu yazılır; kuyruk ve mesaj boyutu sınırlıdır. Aşırı log yükünde tanılama mesajları düşürülebilir. Oyun kayıtları bu log kuyruğunu kullanmaz.
- Taksonomi indeksi değişmemişse yeniden yazılmaz. Görsel kalite, çözünürlük, animasyon sayısı ve performans profili bu çalışma tarafından düşürülmez.
- Derleme mevcut release klasörlerini veya `KozmikData` profillerini silmez. Her paket yeni klasöre çıkar; ZIP ve SHA-256 dosyası üretilir. Unicode/apostrof içeren çalışma yolu için kısa geçici publish alanı kullanılır.

## Sorumluluklar

| Bileşen | Sorumluluk |
| --- | --- |
| `Bridge/BridgeMessage`, `SerialWorkQueue` | Eski/yeni mesaj ayrıştırma, bellek sınırı, FIFO ve kapanış |
| `Bridge/client.js`, `BridgeScript` | Test edilebilir gömülü JS köprüsü ve güvenli hafıza enjeksiyonu |
| `Bridge/CosmicGate.Requests` | Pencere yaşam döngüsü, iş parçacığı geçişi, belge sahipliği |
| `Storage/IStorageApiService`, `FileApiService` | Arayüzden bağımsız dosya komutları |
| `Storage/GameRegistryStore`, `GameDocumentStore`, `GameFolderService` | Kayıt listesi, belgeler/geçmiş, macera klasörleri |
| `Storage/SandboxPathPolicy`, `AtomicFile`, `SqliteDatabase` | Ortak yol, dosya yazma ve bağlantı kuralları |
| `Storage/RootStorageLease` | Aynı kökü kullanan pencerelerin eşzamanlılık sınırı |
| `Transfer/ITransferFilePicker`, `TransferFilePicker` | Aktarım motorunu WPF seçicilerinden ayırma |
| `Diagnostics/BufferedFileLog` | Sınırlı ve toplu tanılama I/O'su |

`PortableGameStore` eski çağıranlar için ince bir uyumluluk cephesidir. Bu çalışma Core'un tüm eski sınıflarının tamamen ayrıştırıldığı anlamına gelmez: özellikle `CosmicGate`, aktarım codec'leri, medya, geliştirici ve ViodCera bileşenlerinde devam edilebilecek büyük sınıflar vardır.

## Uyumluluk sınırı ve kanıt

- `.alek`, eski id'siz `save/reveal`, nesne/JSON-string mesajları, `__alek*` yardımcıları, `fs.*`/`db.*` yanıt biçimleri korunur.
- `Data/meggy.db`, `Games/<macera>/game.db`, belge anahtarları, şema ve son 50 geçmiş kaydı korunur. Zorunlu veri göçü yoktur. SQLite FULL senkronizasyonu azaltılmadı, bağlantı havuzu etkinleştirilmedi.
- R9 kaynaklı bağımsız referans uygulamayla **84 API yanıtı** ve belge geçmişi karşılaştırıldı. R9'un oluşturduğu veritabanı yeni servisle açıldı.
- **13 Core test grubu** geçti: eski mesajlar, kuyruk sınırı/sırası/hata sonrası devam/kapanış, ortak kök kilidi, SQLite, dosya API'si, junction, 260 karakteri aşan yollar, atomik yazma hatası, aktarım turu/iptal ve log sınırı.
- Gerçek `Bridge/client.js` ve JOA `atomic-documents.js`, C# Core SQLite servislerine bağlandı. İki macera izolasyonu, eksik izdüşüm sonrası yeniden açma ve kurtarma geçti. Bu test WebView2 görsel testi değildir.
- Önceki derleme EXE'si gerçek WebView2 ile ayrıca çalıştırıldı: eski kayıt köprüsü, Unicode, SQLite, 20 eşzamanlı dosya isteği, sandbox ve çıkıştan hemen önce gönderilen son yazının tamamlanması geçti.
- JOA'nın gerçek `.alek`, `Alekrythae.App` ve `Assets` dosyalarının izole kopyası aynı eski derlemeyle açıldı: **10/10 modül `started`, uygulama `running`**, son kayıt korunarak çıkış başarılı. Gerçek macera verileri testte kullanılmadı.
- JOA'nın 34 test dosyası tekrar geçti; oluşturulan `.alek` ve manifest güncel.
- Kontrollü 150 ms I/O bekleme senaryosunda çağıran iş parçacığının beklemesi yaklaşık 164 ms iken kuyruğa verme yaklaşık 0,01 ms ölçüldü. Bu **disk hızı, FPS veya toplam işlem süresi kazanımı değildir**; arayüzü disk beklemesinden ayırmayı gösterir.

## Çalıştırma

Core klasöründen:

```powershell
dotnet run --project tests/Core.Compatibility/Core.Compatibility.csproj -c Release
node --test tests/client-contract.test.cjs
.\tools\Build-Core.ps1
.\tools\Test-NativeCore.ps1 -ReleaseDirectory '<üretilen release klasörü>'
.\tools\Test-NativeCore.ps1 -ReleaseDirectory '<üretilen release klasörü>' -Joa
```

JOA köprü testi JOA kaynak ağacının Core'un üst klasöründe bulunmasını bekler. Core birim/uyumluluk testleri bağımsız çalışır. Test verileri `test-output` altında oluşturulur; gerçek maceralar kullanılmaz. `Fixtures/R9PortableGameStore.cs` karşılaştırma referansıdır, yayınlanan uygulamaya eklenmez.

Canlı palet yerleşimi, farklı DPI/ekranlar, GPU sürücüleri ve tüm eski üçüncü taraf `.alek` uygulamaları için ayrıca görsel kabul gerekir. Bilgisayar kontrol aracının native pipe bağlantısı bu oturumda kullanılamadı; bu yüzden elle tıklama/screenshot kabulü tamamlandı sayılmaz.

İlk native deneme araç sandbox'ında WebView2 denetleyicisi kurulurken `E_UNEXPECTED` aldı. Aynı EXE normal Windows izinlerinde hem native köprü testini hem gerçek JOA açılışını geçti. Bu iki sonuç birbirinden ayrı tutulmalıdır; kısıtlı ortamdaki hata ürünün normal Windows açılış hatası olarak raporlanmaz.

Geçmiş test raporunda bir yayın arşivi için 86.019.125 bayt boyutu kaydedilmiştir; bu arşiv güncel v2.1.0 paketi değildir.
SHA-256: `a98a88944bbd3147fe3e2d38172661810c305053fc0b1f9e427c656d946941c7`.
ZIP, native testler başlatılmadan önce üretildi; test sırasında oluşan tarayıcı profili/loglar pakete eklenmedi.

SQLite uzun yol yaklaşımının dayanakları: [SQLite VFS](https://sqlite.org/vfs.html), [Microsoft.Data.Sqlite bağlantı seçenekleri](https://learn.microsoft.com/dotnet/standard/data/sqlite/connection-strings), [Windows uzun yol davranışı](https://learn.microsoft.com/windows/win32/fileio/maximum-file-path-limitation).
# 25 Eylül — İlave JOA entegrasyon doğrulaması

`tools/Test-NativeCore.ps1 -ReleaseDirectory <eski-test-paketi> -Palette` gerçek JOA
CSS/denetleyicisiyle 7 iframe görünüm alanında 4 palet durumu (28 kontrol),
sabit başlık, yatay taşma ve slider güncellemesinde odak/düğüm korumasını geçti.
Bu test OS DPI, fiziksel pointer veya görsel ekran görüntüsü kabulü değildir.

`-Loading` aynı Core'da kopya JOA ve ayrı SQLite kayıtlarıyla
A → hatalı B → B → A geçişini geçti. Son belgeye enjekte edilen native okuma
hatası eski veri referanslarını ve kayıt yollarını korudu; tekrar yükleme ve
kapanırken bekleyen yazının tamamlanması doğrulandı. Test erişimi yalnız geçici
uygulama kopyasına eklenir; gerçek maceralar ve üretim JS'i değiştirilmez.

## 26 Eylül — JOA oturum pencereleri

`-Loading` testi JOA'daki yeni ortak modal modülüyle tekrar geçti. İzole kopyada
A → hatalı B → B → A sırasında eski düzenleme/şifre pencereleri kapandı; geri
dönüşte eski onaylar reddedildi. Güncel pencerede çift tıklama tek commit yaptı;
düzenleme ve şifre onaylarının yazdığı değerler Core SQLite üzerinden yeniden okundu.
`test-output/native-a9006edd/native-result.json`: `ok:true`, `callbackChecks:true`.
Bekleyen son yazıyla kapanış da geçti. Bu adım Core üretim kodunu veya eski yayın ZIP'ini
değiştirmez; JOA çalışma kaynakları ve izole native test kapsamı güncellendi.
