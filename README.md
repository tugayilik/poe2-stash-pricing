# PoE2 Stash Pricer

Path of Exile 2'deki özel stash sekmelerini (Currency, Essence, Runes...) ekrandan tarayıp eşyaları [poe.ninja](https://poe.ninja/docs/api) fiyatlarıyla değerlendiren küçük bir Windows uygulaması. Her sekmenin değerini ve tüm stash'in toplam değerini gösterir, fiyatları oyunun üstüne eşyaların yanına yazar.

## Kurulum

Kurulum yok. `PoeStashPricer.exe`'yi istediğin bir klasöre koyup çalıştır. Windows 10/11'de hazır bulunan .NET Framework 4.8 ile çalışır.

> İmzasız bir exe olduğu için Windows ilk açılışta "Windows kişisel bilgisayarınızı korudu" uyarısı gösterebilir: **Ek bilgi → Yine de çalıştır**.

Ayarlar, kaydettiğin sekmeler ve tarama sonuçları `%APPDATA%\PoeStashPricer\` klasöründe tutulur. Uygulamayı silmek için exe'yi ve bu klasörü silmen yeterli.

## Oyun ayarları

- Ekran modu **Windowed Fullscreen / Borderless** olmalı. Exclusive fullscreen'de ekran görüntüsü siyah gelebilir.
- Oyun dili **İngilizce** olmalı. Eşya isimleri poe.ninja ile İngilizce eşleşiyor.
- Çözünürlük önemli değil. Stash paneli ekranda otomatik bulunur.

## Kullanım

### 1. Sekmeleri kaydet (bir kerelik)

Uygulama hangi sekmenin açık olduğunu senin kaydettiğin ekran görüntüleriyle tanır. Bu yüzden sekmeleri bir kez kaydetmen gerekir:

1. Oyunda stash'i aç.
2. Uygulamada **Sırayla kaydet**'e bas.
3. Uygulama oyunun üstünde hangi sekmeyi açman gerektiğini yazar (örneğin *"Oyunda 'Currency' sekmesini açın ve F6'ya basın"*).
   - Sekmeyi aç ve **tamamen açılmasını bekleyip** **F6**'ya bas. Fare stash'in üstünde olmasın.
   - Sende olmayan bir sekmeyi **F9** ile atla.
4. Bütün sekmeler bitene kadar devam et.

Desteklenen sekmeler: **Currency, Fragments, Expedition, Breach, Abyss, Essence, Delirium, Runes** (Runes, Kalguuran Runes, Soul Cores, Idols, Ancient Augments alt sekmeleri) ve **Ritual**. Fragments'ın 3 alt sekmesinden birini kaydetmek yeterli.

Tek bir sekmeyi yenilemek için listeden seçip **Seçileni kaydet**'e bas ve oyunda F6'ya bas. Bir kaydı silmek için **Sil**.

### 2. Tara

1. Oyunda bir sekme aç ve **F7**'ye bas. Uygulama sekmeyi tanır, eşyaların üstünde fareyi gezdirip **Ctrl+C** ile eşya bilgisini okur.
2. Tarama sırasında fareye dokunma. **Esc** ya da tekrar **F7** taramayı durdurur.
3. Fiyatlar eşyaların üstünde görünür. **F8** fiyat katmanını gizler ya da gösterir.

Her sekmenin son taraması saklanır:

- Başka sekmeye geçince fiyatlar gizlenir, taranmış bir sekmeye dönünce **F7'ye basmadan** geri gelir.
- Bir sekmeyi yeniden taramak istediğinde F7'ye basman yeterli.
- Sonuçlar uygulama kapanınca da kaybolmaz.
- Fiyatlar poe.ninja'dan saatlik güncellenir. Kayıtlı tarama sonuçları her zaman güncel fiyatla hesaplanır.

### Uygulama penceresi

- **En üstte:** tüm taranmış sekmelerin toplam değeri.
- **Solda:** sekmeler, her birinin değeri ve son tarama zamanı. Oyunda açık olan sekme kalın ve ▶ ile işaretli.
- **Sağda:** oyunda açık olan (ya da soldan seçtiğin) sekmenin eşyaları, adetleri ve fiyatları.
- **Göster:** fiyatların Divine, Exalted, Chaos ya da otomatik gösterilmesi.
- **Gecikme (ms):** fare eşyanın üstüne geldikten sonra Ctrl+C'ye kadar bekleme. Eşyalar yanlış ya da eksik okunuyorsa artır (60–100).
- **Önizle:** açık sekmenin tanınıp tanınmadığını ve nerelerin taranacağını gösterir.

## Kısayollar

| Tuş | İş |
|---|---|
| F6 | Açık sekmeyi kaydet (kayıt sırasında) |
| F7 | Açık sekmeyi tara / taramayı durdur |
| F8 | Fiyat katmanını gizle / göster |
| F9 | Kayıt sırasında bu sekmeyi atla |
| Esc | Taramayı durdur |

## Bilmen gerekenler

- **ToS:** Uygulama fareyi otomatik hareket ettirip Ctrl+C gönderir. Oyun sunucusuna bir aksiyon gitmez (sadece üstüne gelip kopyalar), ama otomatik girdi olduğu için GGG kuralları açısından gri alandır. Kullanım sorumluluğu sana ait.
- Oyun **yönetici olarak** çalışıyorsa bu uygulamayı da yönetici olarak aç. Yoksa Windows tuş ve fare gönderimini engeller.
- **Ekrandan okunan adetler:** Bazı eşyalar (Simulacrum, Shattered Triskelion gibi) kopyalanınca adet bilgisi vermez. Uygulama bunların adedini ikonun köşesindeki sayıdan okur. Rakamları taradığın diğer eşyalardan öğrenir, bu yüzden:
  - İlk taramalarını Currency, Essence gibi stack'li eşya çok olan sekmelerle yap.
  - Okuyamadığı adetleri listede **"1?"** diye gösterir. Birkaç sekme daha taradıktan sonra o sekmeyi yeniden tara.
- Rare, Magic ve Unidentified eşyalar fiyatlanmaz ("fiyat yok").
- Kayıtlı olmayan bir sekmeyi de tarayabilirsin, ama sonucu toplam stash değerine eklenmez.

## Geliştirici notları

Kaynak kod `src\` altında. Hiçbir şey kurmadan Windows'un kendi C# derleyicisiyle derlenir:

```bash
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

`tools\detect-test.ps1`, `samples\` klasöründeki tam ekran stash görüntülerinde panel bulma, yuva öğrenme ve sekme tanıma adımlarını oyuna girmeden çalıştırır ve işaretli görselleri `samples\out\` klasörüne yazar.
