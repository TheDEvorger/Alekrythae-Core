# Ałek’ryŧhæ Core 0.2.3

`.alek` uygulamalarını açan ortak Windows runtime/host kaynak kodu. Meggy bu repodan bağımsızdır.

## Derleme

Windows x64 ve .NET 10 SDK gerekir. `BUILD_CORE.cmd` temiz self-contained Windows x64 publish üretir.

## Kullanıcı verisi

Core kaynak reposunda WebView profili, cookie, history, session, cache veya kullanıcı save'i bulunmaz. `KozmikData/`, `bin/`, `obj/`, log ve `.alekdata` çıktıları Git tarafından yok sayılır.

## Meggy ile kullanım

Hazır Windows Core paketindeki `Alekrythae Core.exe` bir kez çalıştırıldığında `.alek` dosya ilişkilendirmesi kayıt edilir. Bundan sonra Meggy ayrı repodan açılabilir.
