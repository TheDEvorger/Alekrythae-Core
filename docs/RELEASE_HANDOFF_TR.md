# 🌌 Ałek’ryŧhæ Core | Teslim ve yayımlama notları

**Hazırlanma tarihi:** 10 Ekim 2026

## Bu kaynak paketinde yapılanlar

- `Alekrythae-Core-v2.1.0.zip` baz alındı; kaynak kodu, JS köprü, testler, resimler, ikonlar ve derleme kaynakları korundu. Buna ek olarak bu kaynak adayında terminal, dosya sistemi ve veri içe aktarma için **sınırlı C# güvenlik değişiklikleri** yapıldı.
- `README.md` iki sağlanan README'nin içerikleri ve gönderilen kaynak dosya yapısı dikkate alınarak genişletildi. Eski README'nin mimari açıklamaları, kayıt/medya/GPU/ViodCera/ConPTY bölümleri korundu; showroom açılışı ve güncel test rehberi eklendi.
- `SECURITY.md` ayrıntılı güvenlik politikası, tehdit modeli ve sorumlu bildirim prosedürüyle yenilendi.
- `.gitignore` .NET, SQLite, WebView2, profil, build, test, arşiv ve sır saklama senaryoları için düzenlendi. Kalıcı sanatsal kaynaklar ve gerekli proje dosyaları ignore edilmiyor.
- Yüklenen `LICENSE.md` **metin olarak değiştirilmeden** GitHub kök dizininde kolay tanınan `LICENSE` adına konuldu. Bu, özel TheDEvorger UPSL v1.3 lisansıdır.
- `.github/workflows/security.yml` (hijyen, sürüm denetimi, Windows compatibility, CodeQL), `.github/workflows/dependency-review.yml`, `.github/dependabot.yml`, `tools/Verify-SourceHygiene.ps1`, `tools/Verify-ReleaseMetadata.ps1` eklendi.
- **v2.1.0 sürüm kimliği tutarlı:** `VERSION`, `.csproj` assembly/file/informational sürümleri, `Program.cs` Windows gösterim sabitleri, `tools/Build-Core.ps1` yayın adı ve `tools/Verify-ReleaseMetadata.ps1` denetimi uyumlu. Son güvenlik sıkılaştırmasında `DeveloperBridge.cs`, `DataTransferService.cs`, `Storage/FileApiService.cs` ve `Storage/SqliteDatabase.cs` değiştirilmiştir. Ayrıntılar: `docs/SECURITY_HARDENING_v2.1.0_TR.md`.
- `RELEASE_NOTES.md` v2.1.0 kaynak sürüm hizalamasını, önceki geliştirme çalışmalarından ve R7 döneminden ayrı anlatıyor.

## Yapılmayanlar

- Windows ortamında `.NET 10` derlemesi ve gerçek WPF/WebView2 başlatma testi tamamlanmadı.
- Uygulamanın önceki çökme/siyah ekran/çalışmama sorununa yönelik neden analizi ve doğrulanmış bir çözüm yapılmadı. Güvenlik değişiklikleri, başlatma sorununu otomatik olarak çözmez.
- Derlenmiş, test edilmiş `.exe` ve hazır kullanıcı release ZIP'i bu teslimin parçası değildir.
- Güvenlik politikası bağımsız penetrasyon testi veya güvenlik sertifikası anlamına gelmez.
- GitHub deposuna otomatik commit/push yapılmadı. Depo üzerinde mevcut kodun yerine bu paketi koymadan önce son çalışan revizyonu ayrı bir branch/tag ile yedekleyin.

## GitHub Desktop ile önerilen uygulama

1. Mevcut repo için bir **backup branch** veya tag oluşturun; çalışan sürümü silmeyin.
2. Bu arşivi ayrı klasöre çıkarıp kaynak farkını inceleyin. Özellikle GitHub'daki eski `src/` klasörü ile bu yeni kaynak ağacını yanlışlıkla birbirine karıştırmayın.
3. Gerekliyse doğrudan bütün kaynak yerine yalnız belgeleri, lisansı, `.gitignore` ve güvenlik araçlarını taşıyın. **README, üzerinde yazdığı gerçek kod sürümüyle uyuşmalıdır.**
4. GitHub Desktop'ta Changes bölümünü kontrol edin: kullanıcı veri tabanları, şifreler, loglar, `release/`, `bin/` ve `obj/` görünmemeli.
5. Windows'ta `BUILD_CORE.cmd` ile derleme yapın. Derlenmeden önce testlerin çalışmasını bekleyin. Ayrıntılar için kökteki `README.md` ve `docs/SECURITY_CHECKLIST.md` dosyasına bakın.
6. Sonuçları doğrulamadan ve `.exe` üzerinde gerçek uygulama açılışı görmeden yeni Release yayımlamayın.
7. Uygulama çalışırsa **binary ZIP** ve onun SHA-256 checksum'unu GitHub Releases'a ekleyin. GitHub'ın otomatik **Source code (zip)** dosyası yalnız commit'teki kaynak kodunu içerir, programı çalıştırmak için yeterli değildir.

## Güvenli tekrar üretim için Windows komutları

```powershell
pwsh -NoProfile -File .\tools\Verify-SourceHygiene.ps1
pwsh -NoProfile -File .\tools\Verify-ReleaseMetadata.ps1

dotnet run --project .\tests\Core.Compatibility\Core.Compatibility.csproj -c Release
.\BUILD_CORE.cmd
```

**Unutma:** `.gitignore` Git'e hiç girmemiş dosyaları filtreler. Geçmişte commit edilmiş sırları otomatik temizlemez. Ayrıca ignore edilen veriler bir GitHub klonuna geri gelmez: kullanıcı verilerinin gerçek yedeklerini ayrı tut.

## Bu ortamda tamamlanan doğrulamalar

- [x] Kaynak sürüm bilgileri `v2.1.0` ile tutarlı; JS/ikon/logo/lisans içerikleri korunmuştur. Bazı C# güvenlik sınırları ve test kaynakları bilinçli değiştirilmiştir; çalışma uyumluluğu henüz Windows'ta doğrulanmamıştır.
- [x] Paket README bağlantılarında var olmayan yerel dosya tespit edilmemiştir.
- [x] Node.js ile gömülü köprü ve JavaScript test/probe dosyalarının **sözdizimi** kontrol edilmiştir (çalışma testleri değil).
- [x] `.gitignore` örneklerinde `bin`, `obj`, `KozmikData`, WebView2 ve sır içeren dosyalar gizlenirken gerçek logo, `.alek` fikstürü ve `client.js` izlemeye açık kalmıştır.
- [x] Yüklenen lisansın içerik SHA-256 değeri değiştirilmemiştir.
- [x] GitHub iş akışları YAML yapısı olarak okunabilmiştir.
- [ ] .NET 10 Windows derleme ve uyumluluk test aracını (bu çalışma ortamında .NET SDK/Windows yok).
- [ ] Gerçek WPF/WebView2 masaüstü, ViodCera/OCR ve JOA çalıştırma testleri.
- [ ] PowerShell denetimlerinin gerçek Windows/GitHub Actions yürütmesi.
- [ ] CodeQL ve GitHub dependency-review iş akışları GitHub üzerinde çalıştırılıp doğrulandı.

Yani bu ZIP düzenlenmiş **kaynak kod arşividir**; onaylanmış, çalışan **release paketi** değildir.

## 10 Ekim 2026 ek güvenlik değişiklikleri

Terminal oturumunun çalışma alanı bırakıldığında kapatılması, bağlantı girdilerini listelememe, SQLite `trusted_schema` ve ZIP/manifest denetimleri eklendi. Yeni arşivdeki `docs/SECURITY_HARDENING_v2.1.0_TR.md` Windows üzerinde yapılması gereken güvenlik testlerini açıklar. Bu paket hâlâ **kaynak kodudur**, derlenmiş Windows release değildir.
