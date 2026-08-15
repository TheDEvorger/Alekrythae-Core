# Ałek’ryŧhæ Core v0.1.2

**`.alek` uygulamaları için ortak Windows çalışma zamanı ve host.**

Ałek’ryŧhæ Core, C# / .NET ve Microsoft WebView2 ile geliştirilen ortak çalışma katmanıdır. Core uygulamadan ayrı tutulur: Core çalışma ortamını sağlar, `.alek` paketi ise uygulamanın kendisini taşır.

## v0.1.2

Bu sürümün ana değişikliği GPU seçimi ve geriye uyumluluktur.

- İlk kullanımda en güçlü algılanan fiziksel GPU otomatik seçilebilir.
- Kullanıcı bir GPU'yu elle seçerse seçim sonraki açılışlarda korunur.
- Ayrık/yüksek performans GPU seçimi WebView2/Chromium tarafına da yüksek performans GPU ipucu iletir.
- Eski `.alek` uygulamalarının `listGraphicsAdapters`, `setGraphicsPreference`, `getGraphicsPreference` ve `openWindowsGraphicsSettings` çağrıları korunur.
- Eski `preference` tabanlı istekler ile yeni `adapterId` tabanlı istekler birlikte desteklenir.
- GPU bir açılışta geçici olarak görünmezse kayıtlı kullanıcı seçimi hemen silinmez.

## Uyumluluk yaklaşımı

GPU güncellemesi grafik köprüsüyle sınırlı tutulmuştur. `.alek` açma sistemi, named pipe, SQLite/PortableGameStore, ExternalMediaBridge, DeveloperBridge, dosya API'leri, pencere davranışı ve suspend/resume sözleşmeleri değiştirilmemiştir. DeveloperBridge protokol revizyonu da eski istemcileri kırmamak için bilerek değiştirilmemiştir.

## Derleme

Windows üzerinde .NET 10 SDK ile:

```cmd
BUILD_CORE.cmd
```

Script self-contained `win-x64` paketini üretir ve sonunda şu dosyayı hazırlar:

```text
release\Alekrythae-Core-v0.1.2-Windows-x64.zip
```

Son kullanıcı için .NET SDK gerekmez; GitHub Release içindeki hazır Windows x64 paketi kullanılır.

## Kaynak yapısı

```text
src/Alekrythae.Core/
Alekrythae.sln
BUILD_CORE.cmd
VERSION
README.md
README_TR.md
CHANGELOG.md
LICENSE.md
SECURITY.md
THIRD_PARTY_NOTICES.md
```

Core ve `.alek` uygulamaları ayrı sürümlenir. Bir uygulamanın kendi repository'si Core binary veya Core kaynak kodunu kopyalamak zorunda değildir.
