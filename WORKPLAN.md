# Plan Pracy: Budowa Launchera Gier z funkcją Suspend/Resume dla Windows 11

Projekt zakłada stworzenie natywnej aplikacji dla systemu Windows 11 w architekturze konsolowej (*Console Experience*). Głównym wyróżnikiem (twistem) jest możliwość pełnego zamrażania (*Suspend*) i wznawiania (*Resume*) procesów gier jednoosobowych podczas przełączania się między nimi, co pozwala oszczędzać zasoby komputera (CPU/GPU) na wzór konsol nowej generacji (np. Xbox Quick Resume).

---

## Faza 1: Środowisko i fundamenty (Setup)
Celem tej fazy jest przygotowanie nowoczesnego środowiska programistycznego oraz architektury projektu wspierającej bindowanie danych w oparciu o stan.

- [ ] **Task 1.1: Instalacja i konfiguracja IDE**
  - **Opis:** Przygotowanie środowiska deweloperskiego.
  - **Kroki:** - Pobierz i zainstaluj **Visual Studio 2022** (wersja Community lub wyższa).
    - W instalatorze (Visual Studio Installer) zaznacz obciążenia (workloads): `.NET desktop development` oraz `Windows application development`.
    - Upewnij się, że na liście komponentów zaznaczony jest *Windows App SDK*.
- [ ] **Task 1.2: Inicjalizacja projektu WinUI 3**
  - **Opis:** Utworzenie szkieletu aplikacji.
  - **Kroki:**
    - Stwórz nowy projekt na podstawie szablonu: **Blank App, Packaged (WinUI 3 in Desktop)**.
    - Wybierz docelowy framework: **.NET 9** (lub .NET 8 LTS, zależnie od stabilności SDK).
    - Nadaj projektowi czytelną nazwę (np. `NexusLauncher` lub `CoreDeck`).
- [ ] **Task 1.3: Konfiguracja wzorca MVVM**
  - **Opis:** Wdrożenie architektury ułatwiającej zarządzanie stanem i UI (odpowiednik podejścia reaktywnego/SwiftUI).
  - **Kroki:**
    - Zainstaluj przez NuGet pakiet: `CommunityToolkit.Mvvm`.
    - Skonfiguruj kontenery wstrzykiwania zależności (Dependency Injection) np. przy użyciu domyślnego `Microsoft.Extensions.DependencyInjection`, aby łatwo przekazywać serwisy do ViewModeli.

---

## Faza 2: Silnik zarządzania procesami (Core - "The Twist")
Najważniejsza i najbardziej wymagająca faza. Odpowiada za bezpośrednią interakcję z systemem operacyjnym i niskopoziomowe zarządzanie stanem gier.

- [ ] **Task 2.1: Mapowanie drzewa procesów gry**
  - **Opis:** Gry często uruchamiają procesy potomne (launchery, procesy crash-reportów itp.). Musimy kontrolować całe "drzewo", a nie tylko główny plik EXE.
  - **Kroki:**
    - Dodaj referencję do `System.Management`.
    - Napisz metodę, która na podstawie ID procesu głównego (PID) odpytuje system przez WMI (`ManagementObjectSearcher` i zapytanie `SELECT * FROM Win32_Process WHERE ParentProcessId = ...`), aby rekurencyjnie znaleźć wszystkie powiązane podprocesy.
- [ ] **Task 2.2: Implementacja mechanizmu Suspend/Resume (P/Invoke)**
  - **Opis:** Wykorzystanie nieudokumentowanych, ale stabilnych funkcji jądra NT do zamrażania wątków.
  - **Kroki:**
    - Stwórz klasę narzędziową (np. `ProcessNativeMethods`).
    - Zaimplementuj importy bibliotek za pomocą P/Invoke dla `ntdll.dll`:
      ```csharp
      [DllImport("ntdll.dll", PreserveSig = false)]
      public static extern void NtSuspendProcess(IntPtr processHandle);

      [DllImport("ntdll.dll", PreserveSig = false)]
      public static extern void NtResumeProcess(IntPtr processHandle);
      ```
    - Napisz bezpieczny wrapper, który otwiera proces za pomocą odpowiednich uprawnień (`PROCESS_SUSPEND_RESUME`) i wykonuje zamrożenie/wznowienie na całej kolekcji procesów zidentyfikowanych w Tasku 2.1.
- [ ] **Task 2.3: Zarządzanie sesją Audio (Audio Muter)**
  - **Opis:** Zapobieganie zapętleniu lub ciągłemu odtwarzaniu bufora dźwiękowego zamrożonej gry w tle.
  - **Kroki:**
    - Zainstaluj bibliotekę **CSCore** lub **NAudio** przez NuGet.
    - Wykorzystaj WASAPI (Windows Audio Session API) do wyszukania sesji dźwiękowej przypisanej do konkretnego PID gry.
    - Zaimplementuj funkcję wyciszania (`SetMute(true)`) tuż przed wywołaniem `NtSuspendProcess` oraz podgłaśniania (`SetMute(false)`) po `NtResumeProcess`.
- [ ] **Task 2.4: Menedżer Okien Win32**
  - **Opis:** Ukrywanie okna zamrożonej gry i automatyczne przywracanie go na pierwszy plan po wznowieniu.
  - **Kroki:**
    - Zaimplementuj importy z `user32.dll`: `ShowWindow`, `SetForegroundWindow`, `ShowWindowAsync`.
    - Zmapuj stany okna: podczas zamrażania minimalizuj okno gry (`SW_MINIMIZE`), a podczas wznawiania przywracaj i forsuj focus (`SW_RESTORE` + `SetForegroundWindow`).

---

## Faza 3: Warstwa danych i integracje (Biblioteka Gier)
Faza odpowiedzialna za automatyczne i ręczne zasilanie bazy danych launchera grami zainstalowanymi na dysku.

- [ ] **Task 3.1: Parser biblioteki Steam**
  - **Opis:** Wyciągnięcie listy gier zainstalowanych lokalnie przez platformę Steam.
  - **Kroki:**
    - Odczytaj z rejestru Windows klucz `HKCU\Software\Valve\Steam\SteamPath`, aby poznać główny folder Steam.
    - Skonstruuj parser dla pliku `steamapps/libraryfolders.vdf`, aby zlokalizować alternatywne foldery instalacji gier (tzw. Steam Libraries).
    - Przeanalizuj pliki `.acf` (`appmanifest_[AppID].acf`) w każdym z tych folderów. Wyciągnij z nich: `name` (Nazwa gry), `installdir` (Folder instalacyjny) oraz `appid` (Potrzebne do uruchomienia gry przez URI `steam://run/[AppID]`).
- [ ] **Task 3.2: Zarządzanie własnymi aplikacjami EXE**
  - **Opis:** Możliwość dodawania gier spoza Steam (GOG, Epic, stare wersje pudełkowe).
  - **Kroki:**
    - Stwórz lokalną bazę danych w formacie JSON zapisywaną w katalogu `%AppData%/TwojaAplikacja/games.json`.
    - Przygotuj model danych zawierający m.in.: `Id` (GUID), `Title`, `ExecutablePath`, `Arguments`, `IconPath`.
- [ ] **Task 3.3: Pobieranie i cache'owanie okładek (Grid Art)**
  - **Opis:** Nadanie launcherowi wyglądu konsolowego poprzez estetyczne grafiki kafelkowe.
  - **Kroki:**
    - Dla gier Steam zaimplementuj pobieranie oficjalnych grafik pionowych z CDN Steam przy użyciu wzorca URL: `https://steamcdn-a.akamaihd.net/steam/apps/[AppID]/library_600x900_2x.jpg`.
    - Dla gier lokalnych zaimplementuj wyciąganie ikony zaszytej w pliku wykonywalnym za pomocą `System.Drawing.Icon.ExtractAssociatedIcon` i konwersję jej do formatu czytelnego dla WinUI 3 (`SoftwareBitmapSource`).

---

## Faza 4: Interfejs użytkownika i sterowanie (Console UI)
Projektowanie warstwy wizualnej zoptymalizowanej pod kątem obsługi z kanapy (interfejs 10-foot UI).

- [ ] **Task 4.1: Projekt makiety głównej w XAML (GridView)**
  - **Opis:** Stworzenie responsywnej siatki gier dostosowanej do rozdzielczości telewizorów/monitorów.
  - **Kroki:**
    - Wykorzystaj kontrolkę `GridView` z odpowiednio zdefiniowanym `ItemTemplate`.
    - Zastosuj `AdaptiveTrigger`, aby układ dopasowywał liczbę kolumn do wielkości ekranu.
    - Ustaw tło aplikacji na ciemne, stonowane barwy (np. głęboki grafit lub granat), zgodne ze standardami nowoczesnych konsol.
- [ ] **Task 4.2: Integracja z Gamepadem (Windows.Gaming.Input)**
  - **Opis:** Pełna obsługa interfejsu za pomocą kontrolera (np. od Xboxa) bez konieczności dotykania myszy.
  - **Kroki:**
    - Dodaj obsługę przestrzeni nazw `Windows.Gaming.Input`.
    - Skonfiguruj zdarzenie `Gamepad.GamepadAdded` i pętlę/timer odpytujący aktualny stan kontrolera.
    - Zmapuj przyciski D-Pad oraz Lewą Gałkę na zmianę zaznaczenia (Focus) wewnątrz `GridView`. Ponieważ WinUI 3 wspiera natywnie tzw. *XY Focus Navigation*, konfiguracja ta sprowadza się do poprawnego ustawienia właściwości `XYFocusKeyboardNavigation="Enabled"` na poziomie kontenera.
- [ ] **Task 4.3: Efekty wizualne i animacje (Feedback)**
  - **Opis:** Dodanie dynamiki i "soczystości" (juice) interfejsowi podczas nawigacji.
  - **Kroki:**
    - Stwórz style w `VisualStateManager` dla kafelków gier.
    - W momencie najechania/zaznaczenia kontrolerem (PointerOver / Focused), kafelek powinien delikatnie się powiększać (np. `Scale` 1.05) i zyskiwać subtelną poświatę (DropShadow), a pozostałe powinny nieznacznie ściemnieć.

---

## Faza 5: Maszyna stanów i User Experience (Wielki Finał)
Połączenie wszystkich modułów w jeden spójny system zarządzania aplikacjami, działający w tle.

- [ ] **Task 5.1: Globalny skrót klawiszowy / Przechwytywanie przycisku Guide**
  - **Opis:** Możliwość wywołania launchera w każdym momencie gry.
  - **Kroki:**
    - Zaimplementuj tzw. Low-Level Keyboard Hook przy użyciu funkcji `SetWindowsHookEx` z `user32.dll` lub wykorzystaj lżejsze API `RegisterHotKey`, aby zarejestrować unikalną kombinację klawiszy (np. `Ctrl + Shift + Escape` lub dedykowany skrót).
    - (Opcjonalnie) Zbadaj przechwytywanie przycisku "Nexus/Guide" (środkowy przycisk Xbox) przy użyciu surowego wejścia XInput.
- [ ] **Task 5.2: Implementacja głównej Maszyny Stanów (State Machine)**
  - **Opis:** Logika decydująca o tym, co dzieje się w systemie po wciśnięciu gorącego klawisza.
  - **Kroki:**
    - Stwórz klasę `LauncherStateManager` kontrolującą stany: `LauncherAkywny`, `GraUruchomiona`, `GraZawieszona`.
    - Zaimplementuj scenariusz przełączenia:
      1. Użytkownik gra w Grę A -> Wciska Hotkey.
      2. Launcher przechwytuje sygnał -> Pobiera PID Gry A.
      3. Wycisza Audio -> Minimalizuje Okno Gry A -> Wywołuje `NtSuspendProcess` na Grze A.
      4. Wyciąga okno Launchera na sam wierzch (`SetForegroundWindow`).
      5. Użytkownik wybiera Grę B -> Launcher wywołuje wznowienie/uruchomienie Gry B -> Sam się minimalizuje.
- [ ] **Task 5.3: Obsługa wyjątków i crashy gier**
  - **Opis:** Zabezpieczenie systemu przed sytuacją, w której gra zawiesi się podczas zamrażania lub zostanie zamknięta siłowo przez użytkownika w tle.
  - **Kroki:**
    - Podepnij zdarzenie `Process.Exited` pod każdy monitorowany proces gry.
    - Jeśli gra zostanie zamknięta z poziomu systemu, launcher musi automatycznie usunąć ją z listy "aktywnych/zamrożonych" w swoim interfejsie, aby uniknąć prób wysłania sygnału `NtResumeProcess` na nieistniejący PID.

---

## Jak zacząć już dzisiaj? (Rekomendowane MVP)

Nie buduj od razu całego UI. Aby upewnić się, że Twój komputer i wybrane gry radzą sobie z tym mechanizmem, wykonaj tzw. **Spike / Proof of Concept**:

1. Stwórz prosty projekt aplikacji konsolowej w C# (.NET 9).
2. Skopiuj kod P/Invoke dla `NtSuspendProcess` oraz `NtResumeProcess` (z Tasku 2.2).
3. Uruchom ręcznie dowolną grę single-player (np. *Wiedźmin 3*, *Cyberpunk 2077* lub coś mniejszego na silniku Unity) w trybie **Borderless Windowed**.
4. Wpisz PID tej gry do swojej aplikacji konsolowej i wywołaj zamrożenie. Sprawdź w *Menedżerze zadań*, czy użycie procesora spadło do 0% i czy gra "zamarła".
5. Wywołaj wznowienie z poziomu konsoli. Jeśli gra powróci do płynnego działania, Twój fundament teoretyczny jest sprawdzony – możesz śmiało przystąpić do budowy pełnego Launchera według powyższego planu!
