# Ałek’ryŧhæ Core

> **`.alek` uygulamaları için ortak çalışma zamanı ve uygulama ana makinesi.**

Ałek’ryŧhæ Core, **C# / .NET** ve **Microsoft WebView2** ile geliştirilen bir Windows uygulama çalışma zamanıdır.

Temel amacı, **`.alek`** biçiminde paketlenen uygulamalara ortak bir çalışma ortamı sağlamak ve bu çalışma ortamını kullanan uygulamalardan bağımsız tutmaktır.

Mimarinin temel fikri oldukça basittir:

**Core çalışma zamanını yönetir. `.alek` uygulamaları ürünü oluşturur.**

Böylece her `.alek` uygulaması kendi başına gelişebilirken aynı Core altyapısını, uygulama yükleme sistemini, yerel veri servislerini, pencere/çalışma zamanı davranışlarını ve WebView2 entegrasyonunu tekrar kullanabilir.

---

## Ałek’ryŧhæ Core nedir?

Ałek’ryŧhæ Core tek amaçlı bir masaüstü uygulaması değildir.

Ałek’ryŧhæ uygulama mimarisinin ortak **runtime** katmanıdır.

Core'un sorumlulukları arasında şunlar bulunur:

- `.alek` uygulama paketlerini açmak ve doğrulamak;
- uygulama manifestlerini okumak ve giriş noktalarını belirlemek;
- WebView2 tabanlı uygulamalara ana makine olmak;
- JavaScript ile yerel C# işlevleri arasında köprü sağlamak;
- uygulamaya ait çalışma zamanı yollarını ve kaynakları yönetmek;
- desteklenen durumlarda ortak yerel depolama/veritabanı servisleri sağlamak;
- uygulama yaşam döngüsünü ve başlangıç davranışını yönetmek;
- uyumlu `.alek` uygulamalarına ortak yerel yetenekler sunmak;
- farklı `.alek` uygulama türleri arasında çalıştırıcı/dispatcher görevi görmek.

Uzun vadeli mimaride `.alek` paketi **teknoloji bağımsız** tutulur.

Bir `.alek` paketi “JavaScript uygulaması” veya “C# uygulaması” olarak tanımlanmak zorunda değildir.  
Paket uygulamayı tanımlar; uygulamanın nasıl çalıştırılacağına Core karar verir.

Bu yapı şu yolları destekleyebilecek şekilde tasarlanmıştır:

- mevcut web çalışma yolu üzerinden **WebView2 / JavaScript uygulamaları**;
- uyumlu bir native giriş noktası bulunan ve WebView2 uygulama yüzeyine ihtiyaç duymadan başlatılabilen **native C# uygulamaları**.

---

## Core neden uygulamalardan ayrı?

Runtime'ı uygulamalardan ayrı tutmak mimariyi daha temiz hale getirir.

Her uygulamanın aynı host ve native entegrasyon kodlarını tekrar tekrar taşıması yerine:

```text
Ałek’ryŧhæ Core
        │
        ├── Uygulama A.alek
        ├── Uygulama B.alek
        ├── Uygulama C.alek
        └── ...
```

Core ortak çalışma ortamı olur.

Bu ayrım sayesinde:

- Core bağımsız güncellenebilir;
- uygulamalar bağımsız sürümlendirilebilir;
- uygulama depoları daha küçük ve temiz tutulabilir;
- aynı runtime kodunun tekrar tekrar kopyalanması önlenebilir;
- ortak native servisler birden fazla uygulama tarafından kullanılabilir;
- kullanıcıya dönük uygulamalar yalnız kendi işlevlerine odaklanabilir;
- `.alek` ekosistemi her uygulamayı dev bir monolitik `.exe` dosyasına çevirmeden büyüyebilir.

---

## `.alek` uygulama modeli

Uyumlu bir `.alek` uygulaması, uygulamanın ihtiyaç duyduğu dosya ve metaveriyi kendi paketinde taşıyabilir.

Uygulamaya göre bir paket şunları içerebilir:

```text
Uygulama.alek
├── manifest
├── uygulama modülleri
├── scriptler
├── kullanıcı arayüzü kaynakları
├── resim / ses / diğer assetler
├── şemalar / metadata
└── uygulamaya özel yapılandırma
```

Paketin kesin içeriği uygulamadan uygulamaya değişebilir.

Buradaki temel mimari kural şudur:

**Uygulama paketi ve Core ayrı bileşenlerdir.**

Core çalışma zamanını sağlar.  
`.alek` paketi uygulamayı sağlar.

---

## Çalıştırma akışı

Basitleştirilmiş başlangıç akışı şöyledir:

```text
Kullanıcı bir .alek uygulamasını açar
                │
                ▼
        Ałek’ryŧhæ Core
                │
                ▼
      Manifesti oku / doğrula
                │
                ▼
       Uygulama türünü belirle
          ┌─────┴──────────┐
          ▼                ▼
    WebView2 / JS       Native C#
      uygulaması         uygulaması
          │                │
          └──────┬─────────┘
                 ▼
         Ortak Core servisleri
```

Uygulamaya özgü mantığın Core içine gömülmesi yerine uygulamanın kendi paketinde kalması hedeflenir.

---

## WebView2 entegrasyonu

Web tabanlı `.alek` uygulamalarında Ałek’ryŧhæ Core uygulama yüzeyi olarak **Microsoft Edge WebView2** kullanır.

Bu sayede modern masaüstü arayüzleri web teknolojileriyle geliştirilebilirken Core üzerinden yerel C# işlevlerine erişim sağlanabilir.

Uygulama tarafında kullanılabilecek teknolojilere örnekler:

- HTML
- CSS
- JavaScript
- modüler JavaScript uygulama kodu
- tarayıcı tabanlı render
- yerel medya ve assetler

Native host sorumluluğu Core'da kalır.

---

## Native köprü

Core'un önemli görevlerinden biri, çalıştırılan uygulama ile Windows/C# tarafı arasındaki iletişimdir.

Kavramsal olarak:

```text
JavaScript Uygulaması
        │
        │ istek
        ▼
     Core Bridge
        │
        ▼
      C# / .NET
        │
        │ sonuç
        ▼
JavaScript Uygulaması
```

Bu yapı sayesinde uyumlu uygulamalar, yerel implementasyonun tamamını kendi içine gömmek zorunda kalmadan desteklenen native işlemleri isteyebilir.

Ortak native işlevlerin tek yerde kalması için köprü Core'un bir parçasıdır.

---

## Yerel-öncelikli tasarım

Ałek’ryŧhæ uygulamaları **local-first / yerel-öncelikli masaüstü kullanımı** temel alınarak tasarlanır.

Uygulama verileri, uygulamanın ve Core mimarisinin sağladığı veya desteklediği depolama mekanizmalarıyla yerel olarak saklanabilir.

Bu repodaki public kaynak veya release paketlerinde geliştiriciye ya da kullanıcıya ait çalışma zamanı verileri bulunmamalıdır.

Örneğin:

- kişisel save dosyaları;
- kullanıcı kayıtlarını içeren uygulama veritabanları;
- browser session verileri;
- WebView2 kullanıcı profilleri;
- cookie'ler;
- giriş/oturum bilgileri;
- browser history;
- cache;
- kişisel loglar;
- özel kullanıcı medyaları

repo içine dahil edilmemelidir.

Çalışma sırasında oluşan kullanıcı verileri kullanıcının bilgisayarına aittir, kaynak kod deposuna değil.

---

## Proje yapısı

Kesin klasör yapısı zamanla değişebilir, ancak Core uygulamaya özel özelliklere göre değil runtime sorumluluklarına göre organize edilir.

Tipik alanlar:

```text
Core
├── uygulama/paket yükleme
├── manifest yönetimi
├── runtime dispatch
├── WebView2 hosting
├── native bridge
├── yerel servisler
├── yaşam döngüsü yönetimi
├── kaynak/runtime yönetimi
└── Windows entegrasyonu
```

Takvim, notlar, oyun sistemleri, editörler, üretkenlik araçları ve benzeri kullanıcı özellikleri Core'a değil ilgili `.alek` uygulamasına aittir.

---

## Örnek uygulama: Ałek’ryŧhæ Meggy

**Ałek’ryŧhæ Meggy**, bu mimari için geliştirilmiş uygulamalardan biridir.

Meggy, Core'dan ayrı geliştirilir ve kendi `.alek` uygulama projesi olarak dağıtılır.

Bu ayrım bilinçlidir:

- **Core reposu:** runtime, hosting ve ortak native altyapı.
- **Meggy reposu:** Meggy'nin UI'si, iş akışları, assetleri ve uygulamaya özel davranışları.

Aynı Core başka uyumlu `.alek` uygulamalarına da host olabilir.

---

## Gereksinimler

Windows çalışma zamanı için:

- Windows 10/11
- kaynaktan derleme için uyumlu .NET Runtime / SDK
- Microsoft Edge WebView2 Runtime
- geliştirme için Visual Studio veya .NET CLI

Kesin framework gereksinimleri için ilgili sürümdeki proje dosyaları esas alınmalıdır.

---

## Kaynak koddan derleme

Repoyu klonla:

```bash
git clone https://github.com/TheDEvorger/Alekrythae-Core.git
cd Alekrythae-Core
```

Ardından .NET CLI ile bağımlılıkları yükleyip derle:

```bash
dotnet restore
dotnet build
```

Release derlemesi için:

```bash
dotnet publish -c Release
```

Kesin publish komutu, mevcut proje yapılandırmasındaki target framework ve runtime identifier değerlerine göre değişebilir.

---

## Core'u kurma / kullanma

Core'un release sürümü Windows sistemine yerleştirilebilir ve `.alek` dosya türüyle ilişkilendirilebilir.

İlişkilendirme yapıldıktan sonra uyumlu bir `.alek` uygulaması Ałek’ryŧhæ Core üzerinden açılabilir.

Kavramsal akış:

```text
Uygulama.alek dosyasına çift tık
              ↓
Windows .alek ilişkilendirmesi
              ↓
      Ałek’ryŧhæ Core
              ↓
        Uygulama açılır
```

Bazı uygulamalar belirli bir Core sürümü gerektirebilir. Tüm geçmiş Core ve uygulama sürümleri arasında uyumluluk garanti edilmez.

---

## Geliştirme ilkeleri

Core geliştirilirken şu ilkeler hedeflenir:

- **Sorumluluğu küçük tutmak** — uygulama özellikleri uygulamalarda kalmalı;
- **Modülerlik** — ortak servisler dev bir sınıfa dönüşmemeli;
- **Yerel-öncelikli çalışma** — bir uygulama açıkça başka bir şey uygulamadığı sürece kullanıcı verisi yerelde kalmalı;
- **Pratiklik** — mimari ürün geliştirmeyi kolaylaştırmalı, gereksiz tören üretmemeli;
- **Genişleyebilirlik** — yeni `.alek` uygulama türleri tüm runtime'ı baştan yazmadan desteklenebilmeli;
- **Performans farkındalığı** — kullanılmayan yüzeyler ve gereksiz arka plan işleri minimumda tutulmalı;
- **Temiz ayrım** — Core ve uygulama kodu zamanla tekrar tek monolite dönüşmemeli.

---

## Mevcut durum

Ałek’ryŧhæ Core aktif geliştirme aşamasındadır.

API'ler, manifest yapıları, runtime davranışları, paket kuralları, bridge operasyonları, uyumluluk kuralları ve iç mimari sürümler arasında değişebilir.

Bir release açıkça belirtmediği sürece geriye veya ileriye dönük uyumluluk varsayılmamalıdır.

---

## Güvenlik ve gizlilik

Public repository ve release paketleri **geliştiricinin kişisel giriş durumu veya çalışma zamanı profili** içermemelidir.

Özellikle WebView2 tarafında oluşabilecek:

- cookie'ler;
- oturum/session verileri;
- local storage;
- browser history;
- önbelleğe alınmış kimlik bilgileri;
- browser cache

repo içine commit edilmemelidir.

Core'u kendin paketliyorsan release oluşturmadan önce runtime sırasında oluşturulan klasörleri kontrol et.

---

## Lisans

Ałek’ryŧhæ Core **source-available proprietary software** olarak yayımlanır.

**Açık kaynak yazılım değildir** ve MIT, Apache, GPL, BSD veya başka bir OSI onaylı açık kaynak lisansı altında yayımlanmaz.

Proje şu lisansla korunur:

**THEDEVORGER UNIVERSAL PROPRIETARY SOFTWARE LICENSE — Version 1.3**  
SPDX kimliği: `LicenseRef-TheDevorger-UPSL-1.3`

Tüm koşullar için [`LICENSE.md`](LICENSE.md) dosyasını oku.

Kaynak kodun görülebilir olması; yeniden dağıtım, ticari kullanım, türev ürün geliştirme, alternatif `.alek` runtime oluşturma veya AI/ML eğitimi için otomatik olarak izin verildiği anlamına gelmez.

---

## Üçüncü taraf bileşenler

Ałek’ryŧhæ Core; .NET ve Microsoft Edge WebView2 gibi üçüncü taraf teknolojilere bağlı olabilir.

Bu bileşenler kendi lisanslarına tabidir.

Uygulanabilir olduğu durumlarda [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) dosyasına bak.

---

## Katkılar

Issue ve teknik geri bildirimler geliştirme sürecinde faydalı olabilir.

Kod veya başka bir materyal göndermeden önce proje lisansındaki katkı koşullarını oku. Katkılar özel lisans hükümlerine tabi olabilir.

---

## Repo dili

Ana README İngilizcedir.

İngilizce sürüm için:

**[README.md](README.md)**

---

## Geliştirici

**TheDevorger**

Lisans / yasal iletişim:  
`thedevorger.alekrythae.dev@gmail.com`

---

<p align="center">
  <strong>Ałek’ryŧhæ Core</strong><br>
  Tek runtime. Birden fazla uygulama. Tek `.alek` mimarisi.
</p>
