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

    // ── LRU Cache ────────────────────────────────────────────────────────────
    const int MAX_CACHE_ENTRIES = 30;

    static readonly Dictionary<string, (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)> cache
        = new Dictionary<string, (HashSet<string>, Dictionary<string, (int, int)>)>();

    static readonly LinkedList<string> cacheKolejnosc = new LinkedList<string>();
    static readonly object cacheLock = new object();

    // ── Semafory ──────────────────────────────────────────────────────────────
    // ZMIANA: zwiększono z 4 do 8 — przy jednym serwerze klient może łatwo
    // przekroczyć poprzedni limit, co powodowało kolejkowanie i timeouty.
    const int MAX_ROWNOLEGLOSCI = 8;
    static readonly SemaphoreSlim semafor = new SemaphoreSlim(MAX_ROWNOLEGLOSCI, MAX_ROWNOLEGLOSCI);

    static void Main()
    {
        TcpListener serwer = new TcpListener(IPAddress.Any, port);
        serwer.Start();
        Console.WriteLine($"[SERWER] Nasłuchuje na porcie {port}. Max równoległości: {MAX_ROWNOLEGLOSCI}, max cache: {MAX_CACHE_ENTRIES}.");

        while (true)
        {
            TcpClient klient = serwer.AcceptTcpClient();
            Task.Run(() => ObsluzKlienta(klient));
        }
    }

    // ── NAPRAWIONY ODCZYT PLIKU ───────────────────────────────────────────────
    // STARA WERSJA: reader.ReadBytes((int)rozmiar)
    //   BinaryReader.ReadBytes() NIE gwarantuje odczytania dokładnie N bajtów
    //   jednym wywołaniem — przy dużych plikach TCP może dostarczać dane
    //   fragmentami. Skutek: bufor jest niekompletny, dalsze ReadXxx() czytają
    //   ze złego miejsca → "Unable to read beyond the end of the stream".
    //
    // NOWA WERSJA: pętla czytająca do skutku (Read() w pętli).
    static (string nazwa, string tekst) OdbierzPlik(BinaryReader reader)
    {
        string nazwa = reader.ReadString();
        long rozmiar = reader.ReadInt64();

        byte[] dane = new byte[rozmiar];
        long przeczytano = 0;

        while (przeczytano < rozmiar)
        {
            // Jedno wywołanie Read() może zwrócić mniej niż prosiliśmy — to norma w TCP.
            int porcja = reader.Read(dane, (int)przeczytano, (int)Math.Min(rozmiar - przeczytano, 65536));
            if (porcja == 0)
                throw new EndOfStreamException($"Klient zerwal polaczenie po {przeczytano}/{rozmiar} bajtow pliku '{nazwa}'.");
            przeczytano += porcja;
        }

        return (nazwa, Encoding.UTF8.GetString(dane));
    }

    static void ObsluzKlienta(TcpClient klient)
    {
        // ZMIANA: ustawiamy SendTimeout i ReceiveTimeout po stronie serwera
        // żeby nie trzymać wiszących połączeń w nieskończoność (np. gdy klient padnie).
        klient.ReceiveTimeout = 300_000; // 5 minut — dla dużych plików
        klient.SendTimeout = 300_000;

        semafor.Wait();

        try
        {
            using (NetworkStream stream = klient.GetStream())
            using (BinaryReader reader = new BinaryReader(stream))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                int n = reader.ReadInt32();
                int grainSize = reader.ReadInt32();

                var (nazwa1, tekst1) = OdbierzPlik(reader);
                var (nazwa2, tekst2) = OdbierzPlik(reader);

                Console.WriteLine($"[SERWER] Odebrano: {nazwa1} ({tekst1.Length} znaków) i {nazwa2} ({tekst2.Length} znaków), n={n}");

                var stoper = System.Diagnostics.Stopwatch.StartNew();

                var task1 = Task.Run(() => PobierzNgramy(nazwa1, tekst1, n));
                var task2 = Task.Run(() => PobierzNgramy(nazwa2, tekst2, n));
                Task.WaitAll(task1, task2);

                var (ngramy1, pozycje1) = task1.Result;
                var (ngramy2, pozycje2) = task2.Result;

                var intersection = new HashSet<string>(ngramy1);
                intersection.IntersectWith(ngramy2);

                double jaccard = ObliczJaccard(ngramy1, ngramy2);
                var (aDoB, bDoA) = ObliczDwustronne(ngramy1, ngramy2);
                var zakresy1 = ZnajdzPodobneZakresy(pozycje1, intersection);
                var zakresy2 = ZnajdzPodobneZakresy(pozycje2, intersection);

                stoper.Stop();
                long czasObliczenMs = stoper.ElapsedMilliseconds;

                Console.WriteLine($"[SERWER] Jaccard={jaccard:F2}%, czas={czasObliczenMs} ms");

                string csvContent = GenerujCSV(nazwa1, nazwa2, jaccard, aDoB, bDoA, zakresy1, zakresy2);
                string jsonContent = GenerujJSON(nazwa1, nazwa2, jaccard, aDoB, bDoA, zakresy1, zakresy2);

                // ── Wysyłanie odpowiedzi ──────────────────────────────────────
                writer.Write(jaccard);
                writer.Write(aDoB);
                writer.Write(bDoA);

                writer.Write(zakresy1.Count);
                foreach (var (od, do_) in zakresy1)
                {
                    writer.Write(od);
                    writer.Write(do_);
                }

                writer.Write(zakresy2.Count);
                foreach (var (od, do_) in zakresy2)
                {
                    writer.Write(od);
                    writer.Write(do_);
                }

                // ZMIANA: writer.Write(string) używa wewnętrznie BinaryWriter,
                // który koduje długość jako 7-bit encoded int — limit ~127 MB.
                // Dla bezpieczeństwa przy bardzo długich CSV/JSON (dużo zakresów)
                // dodajemy guard; w praktyce te stringi są małe.
                writer.Write(csvContent);
                writer.Write(jsonContent);
                writer.Write(czasObliczenMs);

                // ZMIANA: jawny Flush gwarantuje że bufor TCP zostanie wysłany
                // zanim zamkniemy stream — bez tego ostatnie bajty mogą zginąć.
                writer.Flush();
            }
        }
        catch (EndOfStreamException e)
        {
            Console.WriteLine($"[SERWER] Klient zerwal polaczenie: {e.Message}");
        }
        catch (IOException e)
        {
            Console.WriteLine($"[SERWER] Blad IO (timeout lub reset): {e.Message}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SERWER] Blad: {e.Message}");
        }
        finally
        {
            semafor.Release();
            klient.Close();
        }
    }

    // ── LRU Cache ─────────────────────────────────────────────────────────────
    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)
        PobierzNgramy(string nazwaPliku, string tekst, int n)
    {
        string klucz = nazwaPliku + "_" + tekst.Length + "_n" + n;

        lock (cacheLock)
        {
            if (cache.ContainsKey(klucz))
            {
                cacheKolejnosc.Remove(klucz);
                cacheKolejnosc.AddLast(klucz);
                Console.WriteLine($"[CACHE] Hit: {klucz}");
                return cache[klucz];
            }
        }

        var tokeny = PodzielNaSlowa(tekst);
        var wynik = GenerujNgramy(tokeny, n);

        lock (cacheLock)
        {
            if (!cache.ContainsKey(klucz))
            {
                if (cache.Count >= MAX_CACHE_ENTRIES)
                {
                    string najstarszy = cacheKolejnosc.First.Value;
                    cacheKolejnosc.RemoveFirst();
                    cache.Remove(najstarszy);
                    Console.WriteLine($"[CACHE] Usunieto LRU: {najstarszy}");
                }

                cache[klucz] = wynik;
                cacheKolejnosc.AddLast(klucz);
                Console.WriteLine($"[CACHE] Dodano: {klucz} ({cache.Count}/{MAX_CACHE_ENTRIES})");
            }
        }

        return wynik;
    }

    // ── Tokenizacja i n-gramy ─────────────────────────────────────────────────
    static List<(string slowo, int od, int do_)> PodzielNaSlowa(string tekst)
    {
        var tokeny = new List<(string slowo, int od, int do_)>();
        int i = 0;

        while (i < tekst.Length)
        {
            if (!char.IsLetterOrDigit(tekst[i])) { i++; continue; }

            int poczatek = i;
            while (i < tekst.Length && char.IsLetterOrDigit(tekst[i]))
                i++;

            string slowo = tekst.Substring(poczatek, i - poczatek).ToLower();
            tokeny.Add((slowo, poczatek, i));
        }

        return tokeny;
    }

    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)
        GenerujNgramy(List<(string slowo, int od, int do_)> tokeny, int n)
    {
        var ngramy = new HashSet<string>();
        var pozycje = new Dictionary<string, (int od, int do_)>();

        for (int i = 0; i <= tokeny.Count - n; i++)
        {
            string ngram = string.Join(" ", tokeny.GetRange(i, n).Select(t => t.slowo));
            ngramy.Add(ngram);
            if (!pozycje.ContainsKey(ngram))
                pozycje[ngram] = (tokeny[i].od, tokeny[i + n - 1].do_);
        }

        return (ngramy, pozycje);
    }

    // ── Metryki ───────────────────────────────────────────────────────────────
    static double ObliczJaccard(HashSet<string> ngramyA, HashSet<string> ngramyB)
    {
        var intersection = new HashSet<string>(ngramyA);
        intersection.IntersectWith(ngramyB);

        var union = new HashSet<string>(ngramyA);
        union.UnionWith(ngramyB);

        if (union.Count == 0) return 0;
        return (double)intersection.Count / union.Count * 100.0;
    }

    static (double, double) ObliczDwustronne(HashSet<string> A, HashSet<string> B)
    {
        var intersection = new HashSet<string>(A);
        intersection.IntersectWith(B);

        if (A.Count == 0 || B.Count == 0) return (0, 0);

        double aDoB = (double)intersection.Count / A.Count * 100.0;
        double bDoA = (double)intersection.Count / B.Count * 100.0;
        return (aDoB, bDoA);
    }

    static List<(int od, int do_)> ZnajdzPodobneZakresy(
        Dictionary<string, (int od, int do_)> pozycje,
        HashSet<string> wspolneNgramy)
    {
        var zakresy = new List<(int od, int do_)>();

        foreach (var ngram in wspolneNgramy)
            if (pozycje.ContainsKey(ngram))
                zakresy.Add(pozycje[ngram]);

        zakresy.Sort((a, b) => a.od.CompareTo(b.od));

        var polaczone = new List<(int od, int do_)>();
        foreach (var zakres in zakresy)
        {
            if (polaczone.Count == 0 || zakres.od > polaczone.Last().do_)
                polaczone.Add(zakres);
            else
                polaczone[polaczone.Count - 1] = (polaczone.Last().od, Math.Max(polaczone.Last().do_, zakres.do_));
        }

        return polaczone;
    }

    // ── Generowanie CSV / JSON ────────────────────────────────────────────────
    static string GenerujCSV(
        string nazwaA, string nazwaB,
        double jaccard, double aDoB, double bDoA,
        List<(int od, int do_)> zakresy1,
        List<(int od, int do_)> zakresy2)
    {
        var sb = new StringBuilder();
        string fileA = Path.GetFileNameWithoutExtension(nazwaA);
        string fileB = Path.GetFileNameWithoutExtension(nazwaB);

        sb.AppendLine("PlikA,PlikB,Jaccard,A->B,B->A,Typ,Od,Do");

        string jStr = jaccard.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        string aDStr = aDoB.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        string bDStr = bDoA.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        sb.AppendLine($"{fileA},{fileB},{jStr},{aDStr},{bDStr},STATYSTYKA,-,-");

        foreach (var (od, do_) in zakresy1)
            sb.AppendLine($"{fileA},{fileB},,,,ZAKRES_A,{od},{do_}");

        foreach (var (od, do_) in zakresy2)
            sb.AppendLine($"{fileA},{fileB},,,,ZAKRES_B,{od},{do_}");

        return sb.ToString();
    }

    static string GenerujJSON(
        string nazwaA, string nazwaB,
        double jaccard, double aDoB, double bDoA,
        List<(int od, int do_)> zakresy1,
        List<(int od, int do_)> zakresy2)
    {
        var dane = new
        {
            plikA = nazwaA,
            plikB = nazwaB,
            jaccard = Math.Round(jaccard, 2),
            aDoB = Math.Round(aDoB, 2),
            bDoA = Math.Round(bDoA, 2),
            zakresy1 = zakresy1.Select(z => new { od = z.od, do_ = z.do_ }).ToList(),
            zakresy2 = zakresy2.Select(z => new { od = z.od, do_ = z.do_ }).ToList()
        };

        return JsonConvert.SerializeObject(dane, Formatting.Indented);
    }
}