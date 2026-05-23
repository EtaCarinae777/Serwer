using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

/// <summary>
/// Serwer detekcji plagiatów – wersja zoptymalizowana.
///
/// Zmiany względem poprzedniej wersji:
///  1. ngramCache → ConcurrentDictionary (brak globalnego locka przy obliczeniach n-gramów)
///  2. tekstySurowe → ConcurrentDictionary (brak tekstyLock)
///  3. SHA-256 per-wątek via [ThreadStatic] (brak alokacji per żądanie)
///  4. ObsluzKlienta w pełni async (Task zamiast blokującego wątku)
///  5. Semafor async (SemaphoreSlim.WaitAsync)
///  6. Obsługa komendy 0x03 (CROSS-COMPARE): plik B przychodzi inline – serwer
///     przetwarza go lokalnie, bez dodatkowego round-tripu przez sieć
///  7. LRU cache pozostaje pod własnym lockiem (operacje O(1) dzięki LinkedListNode)
///  8. Logowanie nieblokujące (BlockingCollection + dedykowany wątek)
/// </summary>
class Server
{
    // ── Konfiguracja ─────────────────────────────────────────────────────────
    static int port = 8001;
    const int MAX_CACHE_ENTRIES = 200;
    const int MAX_ROWNOLEGLOSCI = 32;   // podniesione z 16 – więcej CPU cores

    // ── SHA-256 per-wątek (brak alokacji per request) ─────────────────────
    [ThreadStatic]
    private static SHA256? _sha256;
    private static SHA256 GetSha() => _sha256 ??= SHA256.Create();

    // ── LRU cache metadanych pliku ────────────────────────────────────────
    // Klucz: fileId (SHA-256 hex)   Wartość: węzeł LRU + nazwa pliku
    static readonly Dictionary<string, (LinkedListNode<string> node, string nazwaPliku)> cache
        = new Dictionary<string, (LinkedListNode<string>, string)>();
    static readonly LinkedList<string> cacheKolejnosc = new LinkedList<string>();
    static readonly object cacheLock = new object();

    // ── Surowe teksty ─────────────────────────────────────────────────────
    // ConcurrentDictionary: bezpieczne bez dodatkowego locka
    static readonly ConcurrentDictionary<string, string> tekstySurowe
        = new ConcurrentDictionary<string, string>(StringComparer.Ordinal);

    // ── Cache n-gramów per (fileId, n) ────────────────────────────────────
    // ConcurrentDictionary: obliczenia mogą zachodzić równolegle dla różnych kluczy
    static readonly ConcurrentDictionary<string,
        (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)> ngramCache
        = new ConcurrentDictionary<string,
            (HashSet<string>, Dictionary<string, (int, int)>)>(StringComparer.Ordinal);

    // ── Semafor async ─────────────────────────────────────────────────────
    static readonly SemaphoreSlim semafor = new SemaphoreSlim(MAX_ROWNOLEGLOSCI, MAX_ROWNOLEGLOSCI);

    // ── Logger nieblokujący ───────────────────────────────────────────────
    static readonly BlockingCollection<string> _logQueue
        = new BlockingCollection<string>(2048);

    static Server()
    {
        var t = new Thread(() =>
        {
            foreach (string msg in _logQueue.GetConsumingEnumerable())
                Console.WriteLine(msg);
        });
        t.IsBackground = true;
        t.Start();
    }

    static void Log(string komunikat)
        => _logQueue.TryAdd($"[{DateTime.Now:HH:mm:ss.fff}] {komunikat}");

    // ── Main ──────────────────────────────────────────────────────────────
    static void Main()
    {
        ThreadPool.SetMinThreads(MAX_ROWNOLEGLOSCI * 2, MAX_ROWNOLEGLOSCI * 2);

        TcpListener serwer = new TcpListener(IPAddress.Any, port);
        serwer.Start();
        Log($"Serwer uruchomiony na porcie {port}. MAX_ROWNOLEGLOSCI={MAX_ROWNOLEGLOSCI}");

        while (true)
        {
            TcpClient klient = serwer.AcceptTcpClient();
            // Nie czekamy – od razu uruchamiamy async obsługę
            _ = ObsluzKlientaAsync(klient);
        }
    }

    // ── Obsługa klienta (async) ───────────────────────────────────────────
    static async Task ObsluzKlientaAsync(TcpClient klient)
    {
        klient.ReceiveTimeout = 300_000;
        klient.SendTimeout = 300_000;

        await semafor.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                using NetworkStream stream = klient.GetStream();
                using BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                using BinaryWriter writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

                byte komenda = reader.ReadByte();
                switch (komenda)
                {
                    case 0x01: ObsluzUpload(reader, writer); break;
                    case 0x02: ObsluzCompare(reader, writer); break;
                    case 0x03: ObsluzCrossCompare(reader, writer); break;
                    default: Log($"Nieznana komenda: 0x{komenda:X2}"); break;
                }
            });
        }
        catch (Exception ex)
        {
            Log($"BŁĄD: {ex.GetType().Name} — {ex.Message}");
        }
        finally
        {
            semafor.Release();
            klient.Close();
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // UPLOAD (0x01)
    // Protokół: [nazwa:string] [rozmiar:int64] [dane:bytes]
    // Odpowiedź: [file_id:string] [juz_w_cache:bool]
    // ════════════════════════════════════════════════════════════════════════
    static void ObsluzUpload(BinaryReader reader, BinaryWriter writer)
    {
        string nazwa = reader.ReadString();
        long rozmiar = reader.ReadInt64();

        byte[] dane = new byte[rozmiar];
        long przeczytano = 0;
        while (przeczytano < rozmiar)
        {
            int porcja = reader.Read(dane, (int)przeczytano,
                (int)Math.Min(rozmiar - przeczytano, 65536));
            if (porcja == 0) throw new EndOfStreamException();
            przeczytano += porcja;
        }

        string fileId = ObliczSHA256(dane);

        // Sprawdź LRU cache
        lock (cacheLock)
        {
            if (cache.TryGetValue(fileId, out var wpis))
            {
                cacheKolejnosc.Remove(wpis.node);
                cacheKolejnosc.AddLast(wpis.node);

                writer.Write(fileId);
                writer.Write(true);
                writer.Flush();
                Log($"UPLOAD (cache hit): {nazwa} → {fileId[..8]}…");
                return;
            }
        }

        // Dekoduj tekst i dodaj do stores – poza lockiem (kosztowne)
        string tekst = Encoding.UTF8.GetString(dane);
        tekstySurowe.TryAdd(fileId, tekst);  // ConcurrentDictionary: thread-safe, bez locka

        lock (cacheLock)
        {
            if (!cache.ContainsKey(fileId))
            {
                EvictIfNeeded_Unsafe();
                var node = cacheKolejnosc.AddLast(fileId);
                cache[fileId] = (node, nazwa);
            }
        }

        writer.Write(fileId);
        writer.Write(false);
        writer.Flush();
        Log($"UPLOAD: {nazwa} ({rozmiar / 1024.0:F1} KB) → {fileId[..8]}…");
    }

    // ════════════════════════════════════════════════════════════════════════
    // COMPARE (0x02) – oba pliki są już na tym serwerze
    // Protokół: [fileId_A:string] [fileId_B:string] [n:int32] [grainSize:int32]
    // ════════════════════════════════════════════════════════════════════════
    static void ObsluzCompare(BinaryReader reader, BinaryWriter writer)
    {
        string fileIdA = reader.ReadString();
        string fileIdB = reader.ReadString();
        int n = reader.ReadInt32();
        int grainSize = reader.ReadInt32();

        string? nazwaA, nazwaB;
        lock (cacheLock)
        {
            if (!cache.TryGetValue(fileIdA, out var wpisA)) { OdeslijBlad(writer, fileIdA); return; }
            if (!cache.TryGetValue(fileIdB, out var wpisB)) { OdeslijBlad(writer, fileIdB); return; }
            nazwaA = wpisA.nazwaPliku;
            nazwaB = wpisB.nazwaPliku;
        }

        ObliczIWyslijWynik(writer, fileIdA, fileIdB, nazwaA!, nazwaB!, n, grainSize);
    }

    // ════════════════════════════════════════════════════════════════════════
    // CROSS-COMPARE (0x03) – plik A jest na serwerze, plik B przychodzi inline
    // Protokół: [fileId_A:string] [nazwaB:string] [rozmiarB:int64] [daneB:bytes]
    //           [n:int32] [grainSize:int32]
    // ════════════════════════════════════════════════════════════════════════
    static void ObsluzCrossCompare(BinaryReader reader, BinaryWriter writer)
    {
        string fileIdA = reader.ReadString();
        string nazwaB = reader.ReadString();
        long rozmiarB = reader.ReadInt64();

        byte[] daneB = new byte[rozmiarB];
        long przeczytano = 0;
        while (przeczytano < rozmiarB)
        {
            int porcja = reader.Read(daneB, (int)przeczytano,
                (int)Math.Min(rozmiarB - przeczytano, 65536));
            if (porcja == 0) throw new EndOfStreamException();
            przeczytano += porcja;
        }

        int n = reader.ReadInt32();
        int grainSize = reader.ReadInt32();

        string? nazwaA;
        lock (cacheLock)
        {
            if (!cache.TryGetValue(fileIdA, out var wpisA)) { OdeslijBlad(writer, fileIdA); return; }
            nazwaA = wpisA.nazwaPliku;
        }

        // Oblicz fileId dla pliku B i dodaj do store'ów tymczasowo (lub jeśli już jest – nic nie rób)
        string fileIdB = ObliczSHA256(daneB);
        string tekstB = Encoding.UTF8.GetString(daneB);
        tekstySurowe.TryAdd(fileIdB, tekstB);

        // Nie dodajemy do LRU cache – to tymczasowy plik tylko na czas tego compare
        // (opcjonalnie można dodać jeśli chcemy cache cross-compare'ów)

        ObliczIWyslijWynik(writer, fileIdA, fileIdB, nazwaA!, nazwaB, n, grainSize);
    }

    // ── Wspólna logika obliczania i wysyłania wyniku ───────────────────────
    static void ObliczIWyslijWynik(
        BinaryWriter writer,
        string fileIdA, string fileIdB,
        string nazwaA, string nazwaB,
        int n, int grainSize)
    {
        var stoper = System.Diagnostics.Stopwatch.StartNew();

        // Równoległe pobieranie n-gramów (ConcurrentDictionary → brak wzajemnego blokowania)
        var task1 = Task.Run(() => PobierzNgramy(fileIdA, n));
        var task2 = Task.Run(() => PobierzNgramy(fileIdB, n));
        Task.WaitAll(task1, task2);

        var (ngramy1, pozycje1) = task1.Result;
        var (ngramy2, pozycje2) = task2.Result;

        var (intersection, jaccard, aDoB, bDoA) = ObliczStatystyki(ngramy1, ngramy2);

        var zakresy1 = ZnajdzPodobneZakresy(pozycje1, intersection);
        var zakresy2 = ZnajdzPodobneZakresy(pozycje2, intersection);

        stoper.Stop();

        string csvContent = GenerujCSV(nazwaA, nazwaB, jaccard, aDoB, bDoA, zakresy1, zakresy2);
        string jsonContent = GenerujJSON(nazwaA, nazwaB, jaccard, aDoB, bDoA, zakresy1, zakresy2);

        writer.Write(jaccard);
        writer.Write(aDoB);
        writer.Write(bDoA);

        writer.Write(zakresy1.Count);
        foreach (var (od, do_) in zakresy1) { writer.Write(od); writer.Write(do_); }

        writer.Write(zakresy2.Count);
        foreach (var (od, do_) in zakresy2) { writer.Write(od); writer.Write(do_); }

        writer.Write(csvContent);
        writer.Write(jsonContent);
        writer.Write(stoper.ElapsedMilliseconds);
        writer.Flush();

        Log($"COMPARE: {nazwaA} vs {nazwaB} (n={n}) → Jaccard={jaccard:F2}% [{stoper.ElapsedMilliseconds}ms]");
    }

    static void OdeslijBlad(BinaryWriter writer, string fileId)
    {
        writer.Write(-1.0);
        writer.Flush();
        Log($"COMPARE błąd: nieznany file_id {fileId[..8]}…");
    }

    // ════════════════════════════════════════════════════════════════════════
    // N-GRAMY – lazy cache bez globalnego locka
    // ════════════════════════════════════════════════════════════════════════
    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)
        PobierzNgramy(string fileId, int n)
    {
        string klucz = string.Concat(fileId, "_n", n.ToString());

        // Szybka ścieżka: już w cache
        if (ngramCache.TryGetValue(klucz, out var cached))
            return cached;

        // Pobierz tekst (ConcurrentDictionary – bez locka)
        if (!tekstySurowe.TryGetValue(fileId, out string? tekst))
            throw new InvalidOperationException($"Brak tekstu dla fileId={fileId[..8]}…");

        var tokeny = PodzielNaSlowa(tekst);
        var wynik = GenerujNgramy(tokeny, n);

        // TryAdd: jeśli inny wątek już dodał – po prostu użyjemy jego wersji (identyczna)
        ngramCache.TryAdd(klucz, wynik);
        return wynik;
    }

    // ════════════════════════════════════════════════════════════════════════
    // LRU eviction (wywoływana wewnątrz lock(cacheLock))
    // ════════════════════════════════════════════════════════════════════════
    static void EvictIfNeeded_Unsafe()
    {
        while (cache.Count >= MAX_CACHE_ENTRIES)
        {
            string najstarszy = cacheKolejnosc.First!.Value;
            cacheKolejnosc.RemoveFirst();
            cache.Remove(najstarszy);
            tekstySurowe.TryRemove(najstarszy, out _);
            // Usuń też wszystkie n-gram cache dla tego fileId
            foreach (var k in ngramCache.Keys.Where(k => k.StartsWith(najstarszy)).ToList())
                ngramCache.TryRemove(k, out _);
        }
    }

    // ════════════════════════════════════════════════════════════════════════
    // SHA-256 (per-wątek – brak alokacji per request)
    // ════════════════════════════════════════════════════════════════════════
    static string ObliczSHA256(byte[] dane)
    {
        byte[] hash = GetSha().ComputeHash(dane);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    // ════════════════════════════════════════════════════════════════════════
    // Tokenizacja
    // ════════════════════════════════════════════════════════════════════════
    static List<(string slowo, int od, int do_)> PodzielNaSlowa(string tekst)
    {
        var tokeny = new List<(string, int, int)>(tekst.Length / 5);
        int i = 0;
        char[] bufor = new char[256];

        while (i < tekst.Length)
        {
            if (!char.IsLetterOrDigit(tekst[i])) { i++; continue; }

            int poczatek = i, len = 0;
            while (i < tekst.Length && char.IsLetterOrDigit(tekst[i]))
            {
                if (len >= bufor.Length) Array.Resize(ref bufor, bufor.Length * 2);
                bufor[len++] = char.ToLowerInvariant(tekst[i++]);
            }
            tokeny.Add((new string(bufor, 0, len), poczatek, i));
        }
        return tokeny;
    }

    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)
        GenerujNgramy(List<(string slowo, int od, int do_)> tokeny, int n)
    {
        int capacity = Math.Max(tokeny.Count - n + 1, 0);
        var ngramy = new HashSet<string>(capacity, StringComparer.Ordinal);
        var pozycje = new Dictionary<string, (int, int)>(capacity, StringComparer.Ordinal);
        var sb = new StringBuilder(n * 16);

        for (int i = 0; i <= tokeny.Count - n; i++)
        {
            sb.Clear();
            for (int k = 0; k < n; k++)
            {
                if (k > 0) sb.Append(' ');
                sb.Append(tokeny[i + k].slowo);
            }
            string ngram = sb.ToString();
            ngramy.Add(ngram);
            if (!pozycje.ContainsKey(ngram))
                pozycje[ngram] = (tokeny[i].od, tokeny[i + n - 1].do_);
        }
        return (ngramy, pozycje);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Statystyki Jaccarda
    // ════════════════════════════════════════════════════════════════════════
    static (HashSet<string> intersection, double jaccard, double aDoB, double bDoA)
        ObliczStatystyki(HashSet<string> A, HashSet<string> B)
    {
        if (A.Count == 0 || B.Count == 0)
            return (new HashSet<string>(), 0, 0, 0);

        var (mniejszy, wiekszy) = A.Count <= B.Count ? (A, B) : (B, A);
        var intersection = new HashSet<string>(mniejszy.Count, StringComparer.Ordinal);

        foreach (string s in mniejszy)
            if (wiekszy.Contains(s))
                intersection.Add(s);

        int intCount = intersection.Count;
        int unionCount = A.Count + B.Count - intCount;

        double jaccard = unionCount == 0 ? 0 : (double)intCount / unionCount * 100.0;
        double aDoB = A.Count == 0 ? 0 : (double)intCount / A.Count * 100.0;
        double bDoA = B.Count == 0 ? 0 : (double)intCount / B.Count * 100.0;

        return (intersection, jaccard, aDoB, bDoA);
    }

    // ════════════════════════════════════════════════════════════════════════
    // Zakresy podobieństwa
    // ════════════════════════════════════════════════════════════════════════
    static List<(int od, int do_)> ZnajdzPodobneZakresy(
        Dictionary<string, (int od, int do_)> pozycje,
        HashSet<string> wspolneNgramy)
    {
        var zakresy = new List<(int, int)>(wspolneNgramy.Count);
        foreach (var ngram in wspolneNgramy)
            if (pozycje.TryGetValue(ngram, out var z))
                zakresy.Add(z);

        zakresy.Sort((a, b) => a.Item1.CompareTo(b.Item1));

        var polaczone = new List<(int, int)>();
        foreach (var z in zakresy)
        {
            if (polaczone.Count == 0 || z.Item1 > polaczone[^1].Item2)
                polaczone.Add(z);
            else
                polaczone[^1] = (polaczone[^1].Item1, Math.Max(polaczone[^1].Item2, z.Item2));
        }
        return polaczone;
    }

    // ════════════════════════════════════════════════════════════════════════
    // Generowanie raportów
    // ════════════════════════════════════════════════════════════════════════
    static string GenerujCSV(
        string nazwaA, string nazwaB,
        double jaccard, double aDoB, double bDoA,
        List<(int od, int do_)> zakresy1, List<(int od, int do_)> zakresy2)
    {
        var sb = new StringBuilder();
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        string fA = Path.GetFileNameWithoutExtension(nazwaA);
        string fB = Path.GetFileNameWithoutExtension(nazwaB);

        sb.AppendLine("PlikA,PlikB,Jaccard,A->B,B->A,Typ,Od,Do");
        sb.AppendLine($"{fA},{fB},{jaccard.ToString("F2", ci)},{aDoB.ToString("F2", ci)},{bDoA.ToString("F2", ci)},STATYSTYKA,-,-");
        foreach (var (od, do_) in zakresy1) sb.AppendLine($"{fA},{fB},,,,ZAKRES_A,{od},{do_}");
        foreach (var (od, do_) in zakresy2) sb.AppendLine($"{fA},{fB},,,,ZAKRES_B,{od},{do_}");

        return sb.ToString();
    }

    static string GenerujJSON(
        string nazwaA, string nazwaB,
        double jaccard, double aDoB, double bDoA,
        List<(int od, int do_)> zakresy1, List<(int od, int do_)> zakresy2)
    {
        return JsonConvert.SerializeObject(new
        {
            plikA = nazwaA,
            plikB = nazwaB,
            jaccard = Math.Round(jaccard, 2),
            aDoB = Math.Round(aDoB, 2),
            bDoA = Math.Round(bDoA, 2),
            zakresy1 = zakresy1.Select(z => new { od = z.od, do_ = z.do_ }).ToList(),
            zakresy2 = zakresy2.Select(z => new { od = z.od, do_ = z.do_ }).ToList()
        }, Formatting.Indented);
    }
}