# HarborOps vodič za početak rada

**Namijenjeno za:** Programere koji počinju rad na projektu HarborOps  
**Svrha:** Objasniti kako je rješenje organizovano, kako bezbjedno razvijati funkcionalnosti unutar njegovih granica i kako lokalno pokrenuti i mijenjati sistem.

---

## 1. HarborOps ukratko

HarborOps je **modularni monolit razvijen u .NET 10** za upravljanje pomorskim operacijama.

Backend se isporučuje kao jedan ASP.NET Core API, ali su poslovne cjeline razdvojene u module. Svaki modul posjeduje svoja poslovna pravila i model podataka. Blazor frontend je zasebno hostovana aplikacija koja sa API-jem komunicira putem HTTP-a.

Osnovna arhitektonska ideja je:

> Zadržati jednostavan način isporuke, uz jasno razdvojene poslovne cjeline u kodu i bazi podataka.

Trenutni poslovni moduli:

| Modul | Odgovornost | SQL šema |
| --- | --- | --- |
| Fleet | Plovila i evidencija flote | `fleet` |
| Crew | Članovi posade i zaduženja | `crew` |
| Voyages | Planiranje i realizacija putovanja | `voyages` |
| Cargo | Teretne operacije | `cargo` |
| Maintenance | Poslovi održavanja | `maintenance` |
| Compliance | Regulatorni poslovi i usklađenost | `compliance` |
| Documents | Upravljanje dokumentima | `documents` |
| Identity | Korisnici, uloge i pristup | `identity` |

U ovom trenutku je **Fleet referentna implementacija**. Ostali moduli imaju pripremljen projekat i marker modula, tako da je moguće započeti razvoj njihovih funkcionalnosti.

---

## 2. Arhitektura

### 2.1 Struktura visokog nivoa

```text
HarborOps.Web
    |
    | HTTP
    v
HarborOps.Api
    +-- povezuje poslovne module
    +-- referencira HarborOps.Infrastructure
    |
    v
HarborOps.Infrastructure
    +-- integracija sa SQL Serverom
    +-- HarborOpsDbContext
    +-- globalni redosljed EF Core migracija
    |
    v
Poslovni moduli
    +-- Domain
    +-- Application
    +-- Infrastructure
    +-- Contracts
    +-- Presentation
```

`HarborOps.Api` i `HarborOps.Infrastructure` referenciraju poslovne module radi sastavljanja aplikacije i pronalaženja konfiguracija perzistencije. **Poslovni moduli nikada ne referenciraju jedni druge.**

### 2.2 Projekti u rješenju

| Projekat | Uloga |
| --- | --- |
| `src/HarborOps.Api` | Ulazna tačka backenda i mjesto sastavljanja aplikacije |
| `src/HarborOps.Web` | Zasebno hostovan Blazor Web frontend |
| `src/HarborOps.Infrastructure` | SQL Server, zajednički `HarborOpsDbContext` i migracije |
| `src/HarborOps.SharedKernel` | Mali i stabilni primitivni tipovi koji se zaista dijele između modula |
| `src/Modules/*` | Po jedan projekat za svaku poslovnu cjelinu |
| `tests/HarborOps.UnitTests` | Brzi testovi poslovnog ponašanja |
| `tests/HarborOps.IntegrationTests` | Integracioni testovi API-ja i perzistencije |
| `tests/HarborOps.ArchitectureTests` | Automatizovane provjere granica između modula |

### 2.3 Struktura modula

Svaki modul je jedan projekat sa sljedećim direktorijumima:

| Direktorijum | Što pripada ovdje |
| --- | --- |
| `Domain` | Entiteti, vrijednosni objekti, invarijante i poslovna pravila |
| `Application` | Orkestracija slučajeva upotrebe i aplikacioni servisi |
| `Infrastructure` | EF Core mapiranja i adapteri specifični za modul |
| `Contracts` | Namjenski request, response i integracioni ugovori |
| `Presentation` | API kontroleri ili definicije endpointa |

Ovo su organizacione granice unutar projekta modula. Ne treba praviti dodatne projekte osim kada veličina modula ili potreba za strožim granicama to opravdavaju.

Proces registracije plovila u Fleet modulu je primjer koji treba pratiti:

```text
POST /api/fleet/vessels
        |
        v
VesselsController                  Presentation
        |
        v
RegisterVesselService             Application
        |
        +--> RegisterVesselValidator
        |
        v
Vessel.Register(...)              Domain
        |
        v
DbContext.Set<Vessel>()
        |
        v
VesselConfiguration              Infrastructure
        |
        v
fleet.Vessels                    SQL Server
```

### 2.4 Model perzistencije

HarborOps ima:

- jednu SQL Server bazu podataka;
- jedan `HarborOpsDbContext`;
- jedan globalno uređen redosljed EF Core migracija;
- po jednu SQL šemu u vlasništvu svakog poslovnog modula.

Konfiguracije entiteta u modulima implementiraju `IEntityTypeConfiguration<T>`. `HarborOpsDbContext` ih pronalazi preko javnog marker tipa svakog modula.

Veze između modula koriste **skalarne identifikatore**, na primjer `VesselId` ili `CrewMemberId`. EF Core navigaciona svojstva između modula nisu dozvoljena.

---

## 3. Lokalno podešavanje

### 3.1 Preduslovi

Potrebno je instalirati ili obezbijediti:

- Git;
- .NET SDK `10.0.302` ili kompatibilnu .NET 10 patch verziju koju bira `global.json`;
- pristup SQL Serveru;
- pristup repozitorijumu;
- IDE, na primjer Visual Studio, Rider ili Visual Studio Code.

Provjera SDK verzije:

```powershell
dotnet --version
```

Iz korijena repozitorijuma vratiti lokalni EF Core alat i zavisnosti:

```powershell
dotnet tool restore
dotnet restore HarborOps.slnx
```

### 3.2 Podešavanje konekcije prema bazi

API čita:

```text
ConnectionStrings:HarborOpsDatabase
```

Repozitorijum trenutno podrazumijeva zajednički razvojni SQL Server konfigurisan u `src/HarborOps.Api/appsettings.json`. Prije pokretanja API-ja ili primjene migracija potrebno je provjeriti mrežni pristup i pristup bazi.

Konekciju je moguće promijeniti za trenutnu PowerShell sesiju bez izmjene fajla koji se prati u repozitorijumu:

```powershell
$env:ConnectionStrings__HarborOpsDatabase = "Server=localhost;Database=HarborOpsDevDb;Integrated Security=True;TrustServerCertificate=True"
```

Lozinke i konekcije specifične za jednog programera ne smiju se commitovati.

### 3.3 Kreiranje ili ažuriranje lokalne baze

Primijeniti sve migracije koje još nisu primijenjene:

```powershell
dotnet tool run dotnet-ef database update `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

### 3.4 Pokretanje aplikacija

Pokretanje API-ja:

```powershell
dotnet run --project src/HarborOps.Api
```

Podrazumijevane razvojne adrese:

- API HTTP: `http://localhost:5204`
- API HTTPS: `https://localhost:7076`
- Health check: `http://localhost:5204/health`
- OpenAPI dokument u Development okruženju: `http://localhost:5204/openapi/v1.json`

Pokretanje Web aplikacije u drugom terminalu:

```powershell
dotnet run --project src/HarborOps.Web
```

Podrazumijevane razvojne adrese:

- Web HTTP: `http://localhost:5087`
- Web HTTPS: `https://localhost:7276`

Web projekat se hostuje zasebno. Trenutno sadrži osnovu frontenda i još uvijek nema konfigurisan API klijent.

### 3.5 Provjera repozitorijuma

Pokrenuti kompletan niz provjera iz korijena repozitorijuma:

```powershell
dotnet restore HarborOps.slnx
dotnet build HarborOps.slnx --no-restore
dotnet test HarborOps.slnx --no-build
dotnet format HarborOps.slnx --verify-no-changes --no-restore
```

Upozorenja se tretiraju kao greške, nullable referentni tipovi su uključeni, a koristi se najnoviji nivo analizatora.

---

## 4. Kako implementirati funkcionalnost

Za standardnu backend funkcionalnost treba pratiti ovaj redosljed:

1. **Odabrati modul koji je vlasnik funkcionalnosti.** Funkcionalnost pripada modulu koji posjeduje poslovno pravilo, a ne API ili Infrastructure projektu.
2. **Modelovati ponašanje domena.** Entitete i poslovna pravila dodati ili izmijeniti unutar `Domain`.
3. **Definisati namjenske ugovore.** API request i response tipove smjestiti u `Contracts`.
4. **Implementirati slučaj upotrebe.** Validaciju, domensko ponašanje i perzistenciju povezati unutar `Application`.
5. **Mapirati perzistenciju.** Dodati interni `IEntityTypeConfiguration<T>` u `Infrastructure` direktorijumu modula i mapirati tabele na šemu tog modula.
6. **Izložiti funkcionalnost.** Kontroler ili endpoint dodati u `Presentation`.
7. **Registrovati servise.** Servise modula registrovati u markeru modula, na primjer u `AddFleetModule()`. API poziva tu metodu u mjestu sastavljanja aplikacije.
8. **Dodati testove.** Poslovno ponašanje pokriti unit testovima, a HTTP i perzistencioni tok integracionim testovima.
9. **Dodati migraciju kada se promijeni model baze.**
10. **Pokrenuti kompletan niz provjera prije otvaranja pull requesta.**

Funkcionalnost treba da bude vertikalna i specifična za poslovnu cjelinu. Fleet modul je trenutni referentni primjer za imenovanje, raspored koda, vidljivost tipova, registraciju, validaciju i testiranje.

---

## 5. Šta treba raditi

- Svaku poslovnu funkcionalnost držati unutar modula koji je njen vlasnik.
- Implementacione tipove modula držati kao `internal`, osim kada predstavljaju namjerni javni ugovor ili marker za sastavljanje aplikacije.
- Za reference na pojmove u vlasništvu drugog modula koristiti skalarne identifikatore.
- Svaku tabelu eksplicitno mapirati na SQL šemu modula koji je njen vlasnik.
- Kada je potrebna perzistencija, unutar modula koristiti zajedničku `DbContext` apstrakciju.
- Poslovna pravila držati blizu domenskog modela.
- Proslijediti `CancellationToken` kroz asinhrone HTTP i operacije nad bazom.
- Dodati fokusirane unit, integracione i arhitektonske testove gdje je potrebno.
- Dodavati package i project reference samo kada ih trenutni kod zaista zahtijeva.
- Verzije paketa centralno održavati u `Directory.Packages.props`.
- Migracije držati malim, pregledanim i usklađenim sa snapshotom EF modela.
- Koristiti opisna imena vezana za funkcionalnost, na primjer `AddCrewCertification`.

---

## 6. Šta ne treba raditi

- Ne dodavati project reference iz jednog poslovnog modula prema drugom.
- Ne praviti EF Core navigaciona svojstva između modula.
- Ne smještati poslovnu logiku u `HarborOps.Api`, kontrolere ili `HarborOps.Infrastructure`.
- Ne praviti drugi aplikacioni `DbContext` niti poseban redosljed migracija za modul.
- Ne smještati migracije u projekte modula.
- Ne pretvarati `HarborOps.SharedKernel` u projekat sa opštim pomoćnim metodama.
- Ne praviti implementacione klase javnim samo da bi im se lakše pristupilo.
- Ne uvoditi generičke repozitorijume, MediatR, event sourcing, message broker, mikroservise ili dodatne slojeve apstrakcije bez eksplicitno odobrene arhitektonske odluke.
- Ne dodavati package reference unaprijed, bez trenutne potrebe.
- Ne commitovati tajne, lozinke ili lične konekcione stringove.
- Ne mijenjati niti brisati migraciju koja je već primijenjena na zajedničko okruženje; napraviti korektivnu migraciju.
- Ne miješati nepovezane izmjene različitih modula u istu migraciju ili pull request kada se mogu razdvojiti.

---

## 7. EF Core migracije

### 7.1 Vlasništvo nad migracijama

Sve migracije pripadaju ovdje:

```text
src/HarborOps.Infrastructure/Persistence/Migrations
```

Iako svaki modul posjeduje svoje tabele i SQL šemu, HarborOps održava **jedan globalni redosljed migracija**. Migracija tehnički može sadržati izmjene iz više modula, ali standardna migracija za funkcionalnost treba da ostane fokusirana na jednu povezanu promjenu.

### 7.2 Prije kreiranja migracije

1. Sinhronizovati branch sa najnovijom verzijom ciljnog brancha.
2. Provjeriti da se aplikacija uspješno build-uje.
3. Provjeriti da su entitet i mapiranje u odgovarajućem modulu.
4. Provjeriti da je svaka nova tabela mapirana na šemu svog modula.
5. Vratiti lokalni alat komandom `dotnet tool restore`.

Na ovaj način se smanjuju konflikti u snapshotu i mogućnost slučajnih, nepovezanih izmjena.

### 7.3 Kreiranje migracije

Pokrenuti iz korijena repozitorijuma:

```powershell
dotnet tool run dotnet-ef migrations add <MigrationName> `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext `
  --output-dir Persistence/Migrations
```

Primjer:

```powershell
dotnet tool run dotnet-ef migrations add AddCrewCertification `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext `
  --output-dir Persistence/Migrations
```

EF Core će ažurirati:

- novi fajl migracije;
- designer fajl migracije;
- `HarborOpsDbContextModelSnapshot.cs`.

Potrebno je pregledati sva tri fajla. Provjeriti imena šema, tabela i kolona, nullability, indekse, strane ključeve, destruktivne operacije i neočekivane izmjene iz drugih modula.

### 7.4 Primjena migracija

Primjena svih migracija koje još nisu primijenjene:

```powershell
dotnet tool run dotnet-ef database update `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

Primjena ili vraćanje lokalne baze na određenu migraciju:

```powershell
dotnet tool run dotnet-ef database update <MigrationName> `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

Vrijednost `0` treba koristiti kao cilj samo kada namjerno želimo poništiti sve migracije u lokalnoj bazi koja se smije obrisati.

### 7.5 Uklanjanje posljednje migracije koja nije dijeljena

Ako posljednja migracija nije ispravna i nije dijeljena niti primijenjena van lokalnog okruženja koje se smije obrisati:

1. Po potrebi vratiti lokalnu bazu na prethodnu migraciju.
2. Ukloniti posljednju migraciju:

```powershell
dotnet tool run dotnet-ef migrations remove `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

Migracija koja je već podijeljena sa timom ne smije se uklanjati. Potrebno je dodati novu migraciju koja je ispravlja.

### 7.6 Generisanje skripte za isporuku

Generisanje idempotentne SQL skripte za pregled ili kontrolisanu isporuku:

```powershell
dotnet tool run dotnet-ef migrations script --idempotent `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext `
  --output HarborOps-Migrations.sql
```

Generisani SQL treba tretirati kao artefakt. Potrebno ga je pregledati prije izvršavanja i ne treba ga commitovati osim kada proces isporuke to eksplicitno zahtijeva.

### 7.7 Paralelne izmjene migracija

Pošto rješenje ima jedan snapshot migracija:

- potrebno je koordinisati rad kada više brancheva mijenja model baze;
- branch treba ažurirati prije generisanja migracije;
- ako dođe do konflikta u snapshotu, poželjno je ukloniti i ponovo generisati sopstvenu migraciju koja još nije dijeljena, nakon spajanja sa najnovijim ciljnim branchom;
- ne treba ručno mijenjati vremenske oznake migracija da bi se nametnuo redosljed;
- nakon rješavanja konflikta migracija potrebno je ponovo pokrenuti build i testove.

---

## 8. Pristup testiranju

### Unit testovi

`tests/HarborOps.UnitTests` se koristi za fokusirane testove poslovnog i aplikacionog ponašanja. Testovi moraju biti brzi i ne treba da zahtijevaju zajednički SQL Server.

### Integracioni testovi

`tests/HarborOps.IntegrationTests` se koristi za HTTP ponašanje, povezivanje kontrolera, serijalizaciju, dependency injection i integraciju perzistencije. Trenutni API test factory mijenja SQL Server izolovanom EF Core in-memory bazom.

### Arhitektonski testovi

`tests/HarborOps.ArchitectureTests` se koristi za sprovođenje strukturnih pravila. Trenutni testovi provjeravaju da moduli ne referenciraju druge module i da Infrastructure pravilno pronalazi sve sklopove modula.

Pokretanje pojedinačnog test projekta tokom razvoja:

```powershell
dotnet test tests/HarborOps.UnitTests/HarborOps.UnitTests.csproj
dotnet test tests/HarborOps.IntegrationTests/HarborOps.IntegrationTests.csproj
dotnet test tests/HarborOps.ArchitectureTests/HarborOps.ArchitectureTests.csproj
```

---

## 9. Kontrolna lista za pull request

Prije zahtjeva za code review treba potvrditi:

- [ ] Funkcionalnost se nalazi u odgovarajućem poslovnom modulu.
- [ ] Nije dodat project reference između poslovnih modula.
- [ ] Veze između modula koriste skalarne identifikatore.
- [ ] Nove tabele koriste SQL šemu modula koji je njihov vlasnik.
- [ ] Implementacioni tipovi su interni osim kada namjerno treba da budu javni.
- [ ] Poslovna pravila se ne nalaze u kontrolerima niti u mjestu sastavljanja aplikacije.
- [ ] Unit i integracioni testovi pokrivaju važno ponašanje.
- [ ] Izmjene baze uključuju jednu pregledanu globalnu migraciju.
- [ ] nisu commitovane tajne niti podešavanja specifična za jednog programera.
- [ ] Restore, build, testovi i provjera formatiranja prolaze.
- [ ] Izmjena je fokusirana i ne uvodi arhitekturu koja nije tražena.

---

## 10. Korisne komande

```powershell
# Vraćanje alata i paketa
dotnet tool restore
dotnet restore HarborOps.slnx

# Build
dotnet build HarborOps.slnx --no-restore

# Testovi
dotnet test HarborOps.slnx --no-build

# Provjera formatiranja
dotnet format HarborOps.slnx --verify-no-changes --no-restore

# Pokretanje API-ja
dotnet run --project src/HarborOps.Api

# Pokretanje Web aplikacije
dotnet run --project src/HarborOps.Web

# Prikaz migracija
dotnet tool run dotnet-ef migrations list `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext

# Primjena migracija koje nisu primijenjene
dotnet tool run dotnet-ef database update `
  --project src/HarborOps.Infrastructure `
  --startup-project src/HarborOps.Api `
  --context HarborOpsDbContext
```

---

## 11. Definicija završenog rada

Izmjena u projektu HarborOps je završena kada:

1. implementira traženo poslovno ponašanje u odgovarajućem modulu;
2. poštuje granice modula i perzistencije;
3. sadrži odgovarajuće automatizovane testove;
4. sadrži pregledanu migraciju kada se mijenja model podataka;
5. uspješno prolazi restore, build, testove i provjeru formatiranja;
6. ne sadrži tajne niti nepovezane arhitektonske izmjene;
7. sljedeći programer može razumjeti izmjenu bez skrivenih koraka za podešavanje.
