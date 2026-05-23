using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

class Server
{
    static int port = 8001;

    const int MAX_CACHE_ENTRIES = 200;

    // Cache n-gramów: file_id (SHA256 hex) → przetworzone dane
    static readonly Dictionary<string, (LinkedListNode<string> node,
        HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje, string nazwaPliku)> cache
        = new Dictionary<string, (LinkedListNode<string>,
            HashSet<string>, Dictionary<string, (int, int)>, string)>();

    static readonly LinkedList<string> cacheKolejnosc = new LinkedList<string>();
    static readonly object cacheLock = new object();

    const int MAX_ROWNOLEGLOSCI = 16;

    static readonly SemaphoreSlim semafor = new SemaphoreSlim(MAX_ROWNOLEGLOSCI, MAX_ROWNOLEGLOSCI);

    // Logger nieblokujący
    static readonly System.Collections.Concurrent.BlockingCollection<string> _logQueue
        = new System.Collections.Concurrent.BlockingCollection<string>(1024);

    static Server()
    {
        var t = new Thread(() =>
        {
            //foreach (string msg in _logQueue.GetConsumingEnumerable())
                //Console.WriteLine(msg);
        });

        t.IsBackground = true;
        t.Start();
    }

    static void Log(string komunikat)
    {
        _logQueue.TryAdd($"[{DateTime.Now:HH:mm:ss}] {komunikat}");
    }

    static void Main()
    {
        TcpListener serwer = new TcpListener(IPAddress.Any, port);

        serwer.Start();
        Log($"Serwer uruchomiony na porcie {port}.");
        Log($"Protokół: UPLOAD <plik> → file_id  |  COMPARE <id_A> <id_B> <n> → wyniki");

        while (true)
        {
            TcpClient klient = serwer.AcceptTcpClient();

            Task.Run(() => ObsluzKlienta(klient));
        }
    }

    static void ObsluzKlienta(TcpClient klient)
    {
        klient.ReceiveTimeout = 300_000;
        klient.SendTimeout = 300_000;
        klient.NoDelay = true;  // też tu dodaj

        semafor.Wait();
        try
        {
            using (NetworkStream stream = klient.GetStream())
            using (BinaryReader reader = new BinaryReader(stream))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                while (true)  // pętla zamiast jednego requestu
                {
                    byte komenda;
                    try { komenda = reader.ReadByte(); }
                    catch { break; }  // klient rozłączył się

                    if (komenda == 0x01)
                        ObsluzUpload(reader, writer);
                    else if (komenda == 0x02)
                        ObsluzCompare(reader, writer);
                    else
                        break;
                }
            }
        }
        finally
        {
            semafor.Release();
            klient.Close();
        }
    }

    // ── UPLOAD ───────────────────────────────────────────────────────────────
    // Protokół: [0x01] [nazwa:string] [rozmiar:int64] [dane:bytes]
    // Odpowiedź: [file_id:string] [juz_w_cache:bool]
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

        // file_id = SHA256 zawartości — niezależny od nazwy pliku
        string fileId = ObliczSHA256(dane);

        lock (cacheLock)
        {
            if (cache.ContainsKey(fileId))
            {
                // Już w cache — aktualizuj LRU i wróć
                var wpis = cache[fileId];

                cacheKolejnosc.Remove(wpis.node);
                cacheKolejnosc.AddLast(wpis.node);

                writer.Write(fileId);
                writer.Write(true); // juz_w_cache
                writer.Flush();

                Log($"UPLOAD (cache hit): {nazwa} → {fileId[..8]}...");
                return;
            }
        }

        // Oblicz n-gramy poza lockiem (może być wolne dla dużych plików)
        string tekst = Encoding.UTF8.GetString(dane);

        var tokeny = PodzielNaSlowa(tekst);

        // Zapisujemy tokeny — n będzie podane przy COMPARE
        // Żeby nie liczyć n-gramów dla każdego n osobno przy uploadzie,
        // przechowujemy tokeny i liczymy lazily.

        // Jednak dla uproszczenia
        // trzymamy dane surowe i przeliczamy przy pierwszym COMPARE.

        // Cache per (fileId, n) jest obsługiwany w ObsluzCompare.

        lock (cacheLock)
        {
            if (!cache.ContainsKey(fileId))
            {
                if (cache.Count >= MAX_CACHE_ENTRIES)
                {
                    string najstarszy = cacheKolejnosc.First.Value;
                    cacheKolejnosc.RemoveFirst();
                    cache.Remove(najstarszy);
                }
                // Tymczasowo zapisujemy z pustymi n-gramami (n nieznane przy uploadzie)
                var node = cacheKolejnosc.AddLast(fileId);

                cache[fileId] = (node, null, null, nazwa);
            }
        }

        // Zapisz surowy tekst do tymczasowego store'u
        lock (tekstyLock)
        {
            if (!tekstySurowe.ContainsKey(fileId))
                tekstySurowe[fileId] = tekst;

        }

        writer.Write(fileId);
        writer.Write(false);

        // nie było w cache
        writer.Flush();

        Log($"UPLOAD: {nazwa} ({rozmiar / 1024.0:F1} KB) → {fileId[..8]}...");
    }

    // Surowe teksty (potrzebne do obliczenia n-gramów przy COMPARE)
    static readonly Dictionary<string, string> tekstySurowe = new Dictionary<string, string>();

    static readonly object tekstyLock = new object();

    // Cache n-gramów per (fileId, n) — klucz: "fileId_n3" itd.

    static readonly Dictionary<string, (HashSet<string> ngramy,
        Dictionary<string, (int od, int do_)> pozycje)> ngramCache
        = new Dictionary<string, (HashSet<string>, Dictionary<string, (int, int)>)>();

    static readonly object ngramCacheLock = new object();

    // ── COMPARE ──────────────────────────────────────────────────────────────
    // Protokół: [0x02] [fileId_A:string] [fileId_B:string] [n:int32] [grainSize:int32]
    // Odpowiedź: [jaccard:double] [aDoB:double] [bDoA:double]
    //            [count1:int32] [od:int32 do:int32]×count1
    //            [count2:int32] [od:int32 do:int32]×count2
    //            [csv:string] [json:string] [czasMs:int64]
    static void ObsluzCompare(BinaryReader reader, BinaryWriter writer)
    {
        string fileIdA = reader.ReadString();
        string fileIdB = reader.ReadString();
        int n = reader.ReadInt32();
        int grainSize = reader.ReadInt32();

        string nazwaA, nazwaB;
        lock (cacheLock)
        {
            if (!cache.ContainsKey(fileIdA))
            {
                writer.Write(-1.0);

                // sygnał błędu: plik nieznany
                writer.Flush();

                Log($"COMPARE błąd: nieznany file_id {fileIdA[..8]}...");
                return;
            }
            if (!cache.ContainsKey(fileIdB))
            {
                writer.Write(-1.0);

                writer.Flush();
                Log($"COMPARE błąd: nieznany file_id {fileIdB[..8]}...");
                return;
            }
            nazwaA = cache[fileIdA].nazwaPliku;

            nazwaB = cache[fileIdB].nazwaPliku;
        }

        var stoper = System.Diagnostics.Stopwatch.StartNew();

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

    // ── N-gramy (lazy, per fileId+n) ─────────────────────────────────────────
    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje) PobierzNgramy(string fileId, int n)
    {
        string klucz = $"{fileId}_n{n}";

        lock (ngramCacheLock)
        {
            if (ngramCache.TryGetValue(klucz, out var wpis))
                return wpis;

        }

        string tekst;

        lock (tekstyLock)
        {
            if (!tekstySurowe.TryGetValue(fileId, out tekst))
                throw new InvalidOperationException($"Brak tekstu dla fileId={fileId[..8]}...");

        }

        var tokeny = PodzielNaSlowa(tekst);
        var wynik = GenerujNgramy(tokeny, n);

        lock (ngramCacheLock)
        {
            if (!ngramCache.ContainsKey(klucz))
                ngramCache[klucz] = wynik;

        }

        return wynik;

    }

    // ── SHA256 ────────────────────────────────────────────────────────────────
    static string ObliczSHA256(byte[] dane)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();

        byte[] hash = sha.ComputeHash(dane);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    // ── Tokenizacja ───────────────────────────────────────────────────────────
    static List<(string slowo, int od, int do_)> PodzielNaSlowa(string tekst)
    {
        var tokeny = new List<(string slowo, int od, int do_)>();

        int i = 0;
        char[] bufor = new char[256];

        while (i < tekst.Length)
        {
            if (!char.IsLetterOrDigit(tekst[i])) { i++; continue; }

            int poczatek = i;
            int len = 0;

            while (i < tekst.Length && char.IsLetterOrDigit(tekst[i]))
            {
                char c = char.ToLowerInvariant(tekst[i]);

                if (len >= bufor.Length)
                    Array.Resize(ref bufor, bufor.Length * 2);

                bufor[len++] = c;
                i++;
            }

            tokeny.Add((new string(bufor, 0, len), poczatek, i));
        }

        return tokeny;
    }

    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)
        GenerujNgramy(List<(string slowo, int od, int do_)> tokeny, int n)
    {
        var ngramy = new HashSet<string>(tokeny.Count);

        var pozycje = new Dictionary<string, (int od, int do_)>(tokeny.Count);
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

    static (HashSet<string> intersection, double jaccard, double aDoB, double bDoA)
        ObliczStatystyki(HashSet<string> A, HashSet<string> B)
    {
        if (A.Count == 0 || B.Count == 0)
            return (new HashSet<string>(), 0, 0, 0);

        var (mniejszy, wiekszy) = A.Count <= B.Count ? (A, B) : (B, A);

        var intersection = new HashSet<string>();

        foreach (string s in mniejszy)
            if (wiekszy.Contains(s))
                intersection.Add(s);

        int intCount = intersection.Count;
        int unionCount = A.Count + B.Count - intCount;

        double jaccard = unionCount == 0 ? 0 : (double)intCount / unionCount * 100.0;
        double aDoB = (double)intCount / A.Count * 100.0;

        double bDoA = (double)intCount / B.Count * 100.0;

        return (intersection, jaccard, aDoB, bDoA);

    }

    static List<(int od, int do_)> ZnajdzPodobneZakresy(
        Dictionary<string, (int od, int do_)> pozycje,
        HashSet<string> wspolneNgramy)
    {
        var zakresy = new List<(int od, int do_)>(wspolneNgramy.Count);

        foreach (var ngram in wspolneNgramy)
            if (pozycje.TryGetValue(ngram, out var z))
                zakresy.Add(z);

        zakresy.Sort((a, b) => a.od.CompareTo(b.od));

        var polaczone = new List<(int od, int do_)>();

        foreach (var z in zakresy)
        {
            if (polaczone.Count == 0 || z.od > polaczone[polaczone.Count - 1].do_)
                polaczone.Add(z);

            else
                polaczone[polaczone.Count - 1] = (polaczone[polaczone.Count - 1].od,
                    Math.Max(polaczone[polaczone.Count - 1].do_, z.do_));
        }

        return polaczone;

    }

    static string GenerujCSV(
        string nazwaA, string nazwaB,
        double jaccard, double aDoB, double bDoA,
        List<(int od, int do_)> zakresy1, List<(int od, int do_)> zakresy2)
    {
        var sb = new StringBuilder();

        string fA = Path.GetFileNameWithoutExtension(nazwaA);
        string fB = Path.GetFileNameWithoutExtension(nazwaB);
        var ci = System.Globalization.CultureInfo.InvariantCulture;

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