# WebView2 PowerShell 5.1 sozdizimi duzeltmesi

## Neden derleme duruyordu?

`tools/Ensure-WebView2.ps1` icindeki tek tirnakli hata metninde `Alekrythae` adinin
kivrimli apostroflu yazimi (`U+2019`) bulunuyordu. Windows PowerShell 5.1,
bu karakteri tek tirnak gibi yorumlayabildigi icin ilk `throw` satiri bozuluyor,
sonraki satirlar da zincirleme parser hatasi uretiyordu.

## Ne degisti?

- `tools/Ensure-WebView2.ps1`: Calistirilabilir betik ASCII karakterleriyle
  sinirlandi. Dosya Windows PowerShell 5.1 ile uyumlu UTF-8 BOM ile saklandi.
- `tools/Build-Core.ps1`: Hem `.NET 10` hem de `WebView2` onkosul betikleri,
  calistirilmadan once PowerShell'in kendi `Parser.ParseFile` API'siyle denetlenir.
- Kaynak surumu **2.1.0** olarak kaldi. C# uygulama kodu, lisans ve onceki
  guvenlik duzeltmeleri degistirilmedi.

## Windows uzerinde kontrol

1. Yeni ZIP'i eski kaynaklarla karistirmadan ayri klasore cikart.
2. `BUILD_CORE.cmd` calistir.
3. `[OK] PowerShell prerequisite syntax verified.` mesajini bekle.
4. WebView2 kuruluysa atlanir; degilse Microsoft imzali kurulum denenir.
5. 15 uyumluluk testinin ve Windows Release derlemesinin bitmesini kontrol et.

Bu kaynak paketi burada Windows/PowerShell 5.1 ile calistirilamadi. Gercek
kurulum ve derleme sonucunu ancak Windows testinde dogrulayabiliriz.
