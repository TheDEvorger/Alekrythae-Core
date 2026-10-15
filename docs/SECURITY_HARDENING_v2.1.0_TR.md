# Ałek’ryŧhæ Core v2.1.0 · Üç odaklı güvenlik sıkılaştırması

> **Durum:** Kaynak kodu güvenlik adayı. Windows üzerinde .NET 10 derlemesi, testler ve `.alek` açılış doğrulaması henüz yapılmadı. Bunlar geçmeden “çalışır release” veya “tam güvenli” olarak yayımlamayın.

## Güvenlik yaklaşımı

Core'un normal dağıtım senaryosunda `.alek` uygulamalarını yayıncı geliştirir. Rastgele üçüncü taraf `.alek` paketlerine yönelik genel bir uygulama mağazası/sandbox tasarlamak bu revizyonun hedefi değil. Bununla birlikte uygulama içeriği, kullanıcıdan seçilen dosyalar, kopyalanan workspace'ler, yedekler ve veritabanları bozuk veya tehlikeli veri içerebilir. Bu nedenle **terminal**, **dosya sistemi** ve **veri aktarma** sınırları ayrı korunur. WebView2 tarafından çalıştırılan JS kodu, Windows üzerinde işlem çalışan Core'un gerçek işletim sistemi yetkilerini doğrudan devralmamalıdır.

## 1. Terminal / geliştirici çalışma alanı

- `dev.workspace.pick`: kullanıcı tarafından seçilen çalışma alanı kökü ve rastgele çalışma alanı anahtarı kullanılır.
- `dev.shell.start`: ilk komut satırı için onay devam eder; onay penceresi terminalin **Windows sandbox'ı olmadığını** açıkça bildirir.
- `elevated = true`: Core onayından ayrı, her denemede **yönetici terminali için özel onay** gerekir; Windows UAC de hâlâ geçerlidir.
- `dev.workspace.release`: o çalışma alanına ait yaşayan ConPTY oturumlarını kapatır. Yalnızca onay anahtarını silip terminali arkada bırakmaz.
- Başlatma öncesi çalışma alanı varlığı ve reparse point kontrolü yeniden yapılır; okunamayan dosya öznitelikleri güvenli kabul edilmez.

**Sınır:** ConPTY içindeki herhangi bir komut, normal Windows kullanıcısının sahip olduğu dosya/ağ erişimini kullanır. `cmd.exe` **izole edilmemiştir**. Core kapatsa bile dışarıda açılmış yönetici terminali `workspace.release` ile geri alınamaz. Gerçek komut sandbox'ı istenirse Windows AppContainer/izole düşük yetkili kullanıcı veya Job Object gibi ayrı bir mimari çalışması gerekir.

## 2. Kök sınırındaki dosya işlemleri

- `fs.list`, junction/reparse-point girdilerini sonuçlara koymaz.
- `fs.writeText`, `fs.writeJson` ve `fs.writeBinary`, eksik klasörler oluşturulduktan sonra hedefin uygulama kökü içinde kaldığını ve ordinary path şartını tekrar denetler.
- Eski `fs.*` işlem isimleri ve başarılı yanıt yapıları değiştirilmedi.

**Sınır:** Eşzamanlı başka bir Windows işlemi klasörleri değiştirirse kontrol ile açma arasında hâlâ TOCTOU yarışı olabilir. Windows tanıtıcıları üzerinden son hedef doğrulayan bir sistem ve güvenlik etiketli süreçler bu revizyona dahil değil.

## 3. SQLite, ZIP, `.alekdata`, yedekler

- Normal SQLite açılışı için `PRAGMA trusted_schema = OFF` kullanılır.
- İçe aktarımın salt-okunur DB bağlantılarında ayrıca `PRAGMA query_only = ON` uygulanır.
- ZIP'lerde yol kaçışı, sürücü/device isimleri, Windows sondaki boşluk/nokta kuralları, tekrar/case-collision yolları ve symlink/reparse-point girdileri reddedilir.
- ZIP girdilerinin gerçek açılmış bayt miktarı hem kendi ilan ettiği boyuta hem de paket toplam sınırına karşı akış sırasında denetlenir. Toplam limit önceki gibi 32 GiB ve 50.000 girdi olarak korunur; limitler dev veri tüketimini **tamamen** önlemez.
- Manifestli v2 paketleri manifest dışı dosya içeremez. Manifest 16 MiB; eski Base64 JSON veri paketleri 512 MiB ile sınırlıdır. Çok büyük veriler için ZIP tabanlı `.alekdata` kullanılır.
- Dışa aktarım `Data/` ve `Games/` içindeki reparse point'leri izlemez; kök klasör bağlantıysa işlemi durdurur.
- Eski manifest içermeyen ZIP göçü sürdürülür; bu geriye dönük destek nedeniyle legacy aktarım formatına modern manifest güvencesi **atfedilmez**.

## Windows doğrulama planı

Repo kökünde PowerShell veya CMD ile:

```powershell
# Gereksinim: Windows 10/11 x64, .NET 10 SDK, WebView2 Runtime
# Önce derleme ve test:
dotnet restore tests/Core.Compatibility/Core.Compatibility.csproj
dotnet run --project tests/Core.Compatibility/Core.Compatibility.csproj -c Release --no-launch-profile

# Daha sonra gerçek Windows release üretimi:
.\BUILD_CORE.cmd
```

Uyumluluk koşucusuna eklenen testlerin kapsamı:

| Test | Beklenti |
|---|---|
| Valid v2 export/import | Tur tamamlanır, veri aynı okunur |
| `../` path | Arşiv reddedilir |
| Aynı path farklı harf büyüklüğü ile | Arşiv reddedilir |
| `CON.txt` / sonda nokta | Arşiv reddedilir |
| Unix symlink girdisi | Arşiv reddedilir |
| V2 manifest dışı dosya | Arşiv reddedilir |
| Junction `fs.list` | Bağlantı sonuçta görünmez |
| Çalışma alanı bırakılması | Bağlı terminal alt süreçleri kapanır (manuel Windows testi gerekir) |
| UAC yükseltme onayı | Core onayı ve Windows UAC ayrı görünür (manuel Windows testi gerekir) |

**Beklenen ek kontrol:** JOA, ViodCera ve geçerli `.alek` uygulamalarında açılış, veri okuma/yazma, aktarım, terminal işlevi, unicode yollar, büyük medya dosyaları, release ZIP ve temiz Windows kullanıcı profilinde açılış. Önceki uygulama açılmama sorununu çözülmüş saymayın.

## Teslim sınırı

- Sürüm kimliği: **v2.1.0** (R-sürüm etiketi eklenmedi).
- Lisans: `LICENSE` dosyası aynen bırakıldı.
- Orijinal resim/font varlıklarına dokunulmadı.
- Derleme/çalıştırma doğrulaması yapılamadı: bu ortamda Windows `.NET 10` araç zinciri yok.
