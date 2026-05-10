using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

class Server
{
    static int port = 8001;

    // ── LRU Cache ────────────────────────────────────────────────────────────
    // Klucz = nazwa pliku + długość tekstu + n
    // Trzymamy maksymalnie MAX_CACHE_ENTRIES wpisów żeby nie zjeść całej RAM.
    // Przy przekroczeniu limitu usuwamy najdawniej używany wpis (LRU).
    const int MAX_CACHE_ENTRIES = 30;

    static readonly Dictionary<string, (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)> cache
        = new Dictionary<string, (HashSet<string>, Dictionary<string, (int, int)>)>();

    // Lista kluczy w kolejności ostatniego użycia (tył = najnowszy)
    static readonly LinkedList<string> cacheKolejnosc = new LinkedList<string>();

    static readonly object cacheLock = new object();

    // ── Semafory ──────────────────────────────────────────────────────────────
    // Ograniczamy liczbę jednoczesnych żądań żeby nie dopuścić do OOM
    // pod dużym obciążeniem. Wartość = ile zadań może działać równolegle.
    const int MAX_ROWNOLEGLOSCI = 4;
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

    static (string nazwa, string tekst) OdbierzPlik(BinaryReader reader)
    {
        string nazwa = reader.ReadString();
        long rozmiar = reader.ReadInt64();
        byte[] dane = reader.ReadBytes((int)rozmiar);
        return (nazwa, Encoding.UTF8.GetString(dane));
    }

    static void ObsluzKlienta(TcpClient klient)
    {
        // Czekamy na wolne miejsce — jeśli serwer jest zajęty, klient poczeka
        // zamiast dostawać OOM przy zbyt wielu równoległych żądaniach.
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

                var stoper = System.Diagnostics.Stopwatch.StartNew();

                // Oba pliki tokenizowane równolegle
                var task1 = Task.Run(() => PobierzNgramy(nazwa1, tekst1, n));
                var task2 = Task.Run(() => PobierzNgramy(nazwa2, tekst2, n));
                Task.WaitAll(task1, task2);

                var (ngramy1, pozycje1) = task1.Result;
                var (ngramy2, pozycje2) = task2.Result;

                // Intersection — część wspólna n-gramów
                var intersection = new HashSet<string>(ngramy1);
                intersection.IntersectWith(ngramy2);

                // Jaccard — podobieństwo w procentach
                double jaccard = ObliczJaccard(ngramy1, ngramy2);

                // Dwustronne podobieństwo
                var (aDoB, bDoA) = ObliczDwustronne(ngramy1, ngramy2);

                // Zakresy podobnych fragmentów
                var zakresy1 = ZnajdzPodobneZakresy(pozycje1, intersection);
                var zakresy2 = ZnajdzPodobneZakresy(pozycje2, intersection);

                stoper.Stop();
                long czasObliczenMs = stoper.ElapsedMilliseconds;

                string csvContent = GenerujCSV(nazwa1, nazwa2, jaccard, aDoB, bDoA, zakresy1, zakresy2);

                // Wyniki liczbowe
                writer.Write(jaccard);
                writer.Write(aDoB);
                writer.Write(bDoA);

                // Zakresy pliku 1
                writer.Write(zakresy1.Count);
                foreach (var (od, do_) in zakresy1)
                {
                    writer.Write(od);
                    writer.Write(do_);
                }

                // Zakresy pliku 2
                writer.Write(zakresy2.Count);
                foreach (var (od, do_) in zakresy2)
                {
                    writer.Write(od);
                    writer.Write(do_);
                }

                // JSON bez pełnych tekstów — klient je już ma lokalnie
                string jsonContent = GenerujJSON(
                    nazwa1, nazwa2,
                    jaccard, aDoB, bDoA,
                    zakresy1, zakresy2);

                writer.Write(csvContent);
                writer.Write(jsonContent);
                writer.Write(czasObliczenMs);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SERWER] Blad: {e.Message}");
        }
        finally
        {
            // Zawsze zwalniamy semafor — nawet przy wyjątku
            semafor.Release();
            klient.Close();
        }
    }

    // ── LRU Cache — pobieranie / wstawianie ──────────────────────────────────
    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)
        PobierzNgramy(string nazwaPliku, string tekst, int n)
    {
        string klucz = nazwaPliku + "_" + tekst.Length + "_n" + n;

        lock (cacheLock)
        {
            if (cache.ContainsKey(klucz))
            {
                // Przenosimy klucz na koniec listy (najnowszy)
                cacheKolejnosc.Remove(klucz);
                cacheKolejnosc.AddLast(klucz);
                return cache[klucz];
            }
        }

        // Obliczamy poza lockiem żeby nie blokować innych wątków
        var tokeny = PodzielNaSlowa(tekst);
        var wynik = GenerujNgramy(tokeny, n);

        lock (cacheLock)
        {
            if (!cache.ContainsKey(klucz))
            {
                // Jeśli cache jest pełny — wyrzucamy najdawniej używany wpis (LRU)
                if (cache.Count >= MAX_CACHE_ENTRIES)
                {
                    string najstarszy = cacheKolejnosc.First.Value;
                    cacheKolejnosc.RemoveFirst();
                    cache.Remove(najstarszy);
                    Console.WriteLine($"[CACHE] Usunieto najstarszy wpis: {najstarszy}");
                }

                cache[klucz] = wynik;
                cacheKolejnosc.AddLast(klucz);
                Console.WriteLine($"[CACHE] Dodano: {klucz} (rozmiar cache: {cache.Count}/{MAX_CACHE_ENTRIES})");
            }
        }

        return wynik;
    }

    static int LiczSlowa(string tekst)
    {
        string[] slowa = tekst.Split(new char[] { ' ', '\t', '\n', '\r' },
                                     StringSplitOptions.RemoveEmptyEntries);
        return slowa.Length;
    }

    static double PorownajPliki(string tekstA, string tekstB, int n)
    {
        return 0.0;
    }

    static List<(string slowo, int od, int do_)> PodzielNaSlowa(string tekst)
    {
        var tokeny = new List<(string slowo, int od, int do_)>();
        int i = 0;

        while (i < tekst.Length)
        {
            if (!char.IsLetterOrDigit(tekst[i]))
            {
                i++;
                continue;
            }

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

        if (A.Count == 0 || B.Count == 0)
            return (0, 0);

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
        {
            if (pozycje.ContainsKey(ngram))
                zakresy.Add(pozycje[ngram]);
        }

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

    static string GenerujCSV(
        string nazwaA,
        string nazwaB,
        double jaccard,
        double aDoB,
        double bDoA,
        List<(int od, int do_)> zakresy1,
        List<(int od, int do_)> zakresy2)
    {
        StringBuilder sw = new StringBuilder();

        string fileA = Path.GetFileNameWithoutExtension(nazwaA);
        string fileB = Path.GetFileNameWithoutExtension(nazwaB);

        sw.AppendLine("PlikA,PlikB,Jaccard,A->B,B->A,Typ,Od,Do");

        string jaccardStr = jaccard.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        string aDoBStr = aDoB.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        string bDoAStr = bDoA.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        sw.AppendLine($"{fileA},{fileB},{jaccardStr},{aDoBStr},{bDoAStr},STATYSTYKA,-,-");

        foreach (var (od, do_) in zakresy1)
            sw.AppendLine($"{fileA},{fileB},,,,ZAKRES_A,{od},{do_}");

        foreach (var (od, do_) in zakresy2)
            sw.AppendLine($"{fileA},{fileB},,,,ZAKRES_B,{od},{do_}");

        return sw.ToString();
    }

    // JSON bez pełnych tekstów plików — klient je już ma lokalnie,
    // nie ma sensu przesyłać ich z powrotem przez sieć (ogromna oszczędność RAM i pasma).
    static string GenerujJSON(
        string nazwaA,
        string nazwaB,
        double jaccard,
        double aDoB,
        double bDoA,
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

    static bool CzyPlagiat(double podobienstwo, double prog)
    {
        return podobienstwo >= prog;
    }
}