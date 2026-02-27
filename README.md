## Fiskodo – Discord Music Bot & Dashboard

Bu repo iki ana parçadan oluşur:
- **fiskodo.backend**: ASP.NET Core Web API + Discord müzik botu
- **fiskoda.dashboard**: React (Vite) tabanlı web dashboard

Bu doküman özellikle **Junior Developer**'lar için yazıldı; adım adım ilerleyerek projeyi ayağa kaldırabilirsin.

---

## 1. Gereksinimler

- **.NET SDK**: 8 veya üzeri
- **Node.js**: 18+ (LTS önerilir)
- **npm** veya **pnpm** / **yarn**
- Bir **Discord Bot Token**
- Çalışan bir **Lavalink** sunucusu (host, port, password bilgileri)

---

## 2. Proje Yapısı

```
Fiskodo/
  fiskodo.backend/      # ASP.NET Core Web API + Discord bot
  fiskoda.dashboard/    # React + Vite dashboard (SPA)
  PLAN.MD               # Projenin teknik planı / notlar
  .gitignore            # Ortak ignore kuralları
  README.md             # Bu dosya
```

### 2.1 Backend klasörü (`fiskodo.backend`)

Ana klasörler:
- `Controllers/`  
  - HTTP endpoint'ler burada (ör. Auth, BotStatus).
- `Services/`  
  - Bot, müzik yönetimi, queue yönetimi gibi iş mantığı sınıfları.
- `Program.cs`  
  - Uygulamanın giriş noktası, DI kayıtları, Swagger, JWT, Discord, Lavalink konfigürasyonu.
- `appsettings.json`  
  - **Örnek / template** konfigürasyon. Gerçek şifreler burada tutulmamalı.
- `appsettings.Local.json`  
  - **Sadece senin bilgisayarına özel** konfigürasyon (Git'e commit EDİLMEZ).

### 2.2 Frontend klasörü (`fiskoda.dashboard`)

Ana dosyalar:
- `src/main.tsx`  
  - React uygulamasının giriş noktası, router ve toast provider burada.
- `src/App.tsx` (ve diğer component dosyaları)  
  - Sayfa ve layout bileşenleri.
- `package.json`  
  - Script'ler ve bağımlılıklar.

---

## 3. Konfigürasyon (appsettings)

### 3.1 appsettings.json (template)

`fiskodo.backend/appsettings.json` **örnek** değerler taşır:
- `Discord:Token`
- `Lavalink:BaseAddress`
- `Lavalink:Passphrase`
- `Jwt:Secret`
- `Auth:Username` / `Auth:Password`

Bu dosya repoda kalır ama gerçek gizli bilgileri içermez.

### 3.2 appsettings.Local.json (lokal ayarların)

`appsettings.Local.json` dosyası:
- `fiskodo.backend` içinde yer alır.
- `.gitignore` içinde olduğu için **Git'e commit edilmez**.
- Senin makinene özel gerçek token, password vb. değerleri içerir.

**Adımlar:**
1. `fiskodo.backend` klasörüne git.
2. `appsettings.Local.json` dosyasını aç.
3. Aşağıdaki alanları kendi ortamına göre doldur:
   - `Discord:Token`
   - `Lavalink:BaseAddress`
   - `Lavalink:Passphrase`
   - `Jwt:Secret`
   - `Auth:Username` / `Auth:Password`

> Not: Şu an `Program.cs` doğrudan `appsettings.json` üzerinden okuma yapıyor. En basit yaklaşım junior'lar için:
> - `appsettings.json` ve `appsettings.Local.json` içeriğini **senkron** tutmak (önce Local'de güncelle, sonra aynı değerleri template'e maskelemeden kopyalama).
> Daha ileri seviyede, `Program.cs` içinde `appsettings.Local.json`'ı opsiyonel olarak ekleyip sadece Local'i gerçek değerlerle doldurmak tavsiye edilir.

---

## 4. Backend'i Çalıştırma (fiskodo.backend)

1. Terminali projenin kök dizininde aç (`Fiskodo/`).
2. Backend klasörüne geç:
   ```bash
   cd fiskodo.backend
   ```
3. Gerekirse NuGet paketlerini indir:
   ```bash
   dotnet restore
   ```
4. Uygulamayı çalıştır:
   ```bash
   dotnet run
   ```
5. Çalıştığında:
   - API varsayılan olarak `https://localhost:5001` veya benzeri bir portta ayağa kalkar.
   - Discord botu background'da gateway'e bağlanır.
   - Development ortamında Swagger arayüzüne gidebilirsin (örn. `/swagger`).

---

## 5. Frontend'i Çalıştırma (fiskoda.dashboard)

1. Yeni bir terminal aç.
2. Dashboard klasörüne geç:
   ```bash
   cd fiskoda.dashboard
   ```
3. Bağımlılıkları yükle:
   ```bash
   npm install
   # veya
   # pnpm install
   # yarn
   ```
4. Geliştirme modunu çalıştır:
   ```bash
   npm run dev
   ```
5. Vite genelde `http://localhost:5173` adresinde ayağa kalkar.

> `Program.cs` içinde CORS ayarı olarak `http://localhost:5173` zaten açık, bu yüzden frontend backend'e rahatça istek atabilir.

---

## 6. Build & Deployment (özet)

- **Backend publish**:
  ```bash
  cd fiskodo.backend
  dotnet publish -c Release
  ```
- **Frontend build**:
  ```bash
  cd fiskoda.dashboard
  npm run build
  ```
- Frontend build çıktısı (`dist/` klasörü) bir web sunucuya (veya backend'in `wwwroot`'una) deploy edilebilir.

---

## 7. Junior Developer için İpuçları

- **Adım adım git**: Önce backend'i sorunsuz çalıştır, sonra frontend'i bağla.
- **Loglara bak**: Hata aldığında önce terminal ve uygulama loglarını incele.
- **Config değiştirirken**: 
  - Gerçek değerleri **sadece** `appsettings.Local.json` içinde güncelle.
  - `appsettings.json` içinde sadece örnek / maskeleme değerler bırak.
- **Soru işareti olduğunda** `PLAN.MD` dosyasına göz at; mimari kararlar orada özetlenmiş.

