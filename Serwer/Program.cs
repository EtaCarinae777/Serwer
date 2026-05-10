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

    const int MAX_CACHE_ENTRIES = 30;

    static readonly Dictionary<string, (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)> cache
        = new Dictionary<string, (HashSet<string>, Dictionary<string, (int, int)>)>();

    static readonly LinkedList<string> cacheKolejnosc = new LinkedList<string>();
    static readonly object cacheLock = new object();

    const int MAX_ROWNOLEGLOSCI = 8;
    static readonly SemaphoreSlim semafor = new SemaphoreSlim(MAX_ROWNOLEGLOSCI, MAX_ROWNOLEGLOSCI);

    static void Main()
    {
        TcpListener serwer = new TcpListener(IPAddress.Any, port);
        serwer.Start();

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

        byte[] dane = new byte[rozmiar];
        long przeczytano = 0;

        while (przeczytano < rozmiar)
        {
            int porcja = reader.Read(dane, (int)przeczytano, (int)Math.Min(rozmiar - przeczytano, 65536));
            if (porcja == 0)
                throw new EndOfStreamException();
            przeczytano += porcja;
        }

        return (nazwa, Encoding.UTF8.GetString(dane));
    }

    static void ObsluzKlienta(TcpClient klient)
    {
        klient.ReceiveTimeout = 300_000;
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

                var stoper = System.Diagnostics.Stopwatch.StartNew();

                var task1 = Task.Run(() => PobierzNgramy(nazwa1, tekst1, n));
                var task2 = Task.Run(() => PobierzNgramy(nazwa2, tekst2, n));
                Task.WaitAll(task1, task2);

                var (ngramy1, pozycje1) = task1.Result;
                var (ngramy2, pozycje2) = task2.Result;

                // OPTYMALIZACJA: Zamiast tworzyć kopię HashSetu żeby policzyć
                // przecięcie, iterujemy po mniejszym zbiorze i sprawdzamy Contains
                // w większym. O(min(|A|,|B|)) zamiast O(|A|) alokacji + kopiowania.
                var (intersection, jaccard, aDoB, bDoA) = ObliczStatystyki(ngramy1, ngramy2);

                var zakresy1 = ZnajdzPodobneZakresy(pozycje1, intersection);
                var zakresy2 = ZnajdzPodobneZakresy(pozycje2, intersection);

                stoper.Stop();
                long czasObliczenMs = stoper.ElapsedMilliseconds;

                string csvContent = GenerujCSV(nazwa1, nazwa2, jaccard, aDoB, bDoA, zakresy1, zakresy2);
                string jsonContent = GenerujJSON(nazwa1, nazwa2, jaccard, aDoB, bDoA, zakresy1, zakresy2);

                writer.Write(jaccard);
                writer.Write(aDoB);
                writer.Write(bDoA);

                writer.Write(zakresy1.Count);
                foreach (var (od, do_) in zakresy1) { writer.Write(od); writer.Write(do_); }

                writer.Write(zakresy2.Count);
                foreach (var (od, do_) in zakresy2) { writer.Write(od); writer.Write(do_); }

                writer.Write(csvContent);
                writer.Write(jsonContent);
                writer.Write(czasObliczenMs);
                writer.Flush();
            }
        }
        catch { }
        finally
        {
            semafor.Release();
            klient.Close();
        }
    }

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
                }
                cache[klucz] = wynik;
                cacheKolejnosc.AddLast(klucz);
            }
        }

        return wynik;
    }

    // OPTYMALIZACJA: używamy char[] buffer zamiast string.Substring() w pętli.
    // Substring() alokuje nowy string dla każdego słowa — dla dużych plików
    // to miliony alokacji. Teraz konwertujemy znaki bezpośrednio do lowercase
    // w buforze i tworzymy string tylko raz.
    static List<(string slowo, int od, int do_)> PodzielNaSlowa(string tekst)
    {
        var tokeny = new List<(string slowo, int od, int do_)>();
        int i = 0;
        char[] bufor = new char[256]; // reużywany bufor dla słów

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

    // OPTYMALIZACJA: string.Join() w pętli dla każdego ngramu jest wolne —
    // tworzy tablicę pośrednią i alokuje nowy string przy każdym wywołaniu.
    // Używamy StringBuilder ze stałym rozmiarem, co redukuje alokacje.
    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)
        GenerujNgramy(List<(string slowo, int od, int do_)> tokeny, int n)
    {
        var ngramy = new HashSet<string>(tokeny.Count);
        var pozycje = new Dictionary<string, (int od, int do_)>(tokeny.Count);
        var sb = new StringBuilder(n * 16); // szacowana pojemność

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

    // OPTYMALIZACJA: Poprzednio ObliczJaccard i ObliczDwustronne każda z osobna
    // robiła "new HashSet<string>(ngramyA)" (kopia całego zbioru!) i IntersectWith.
    // To 2 pełne kopie + 2 iteracje przez dziesiątki tysięcy elementów.
    //
    // Teraz liczymy przecięcie raz: iterujemy po MNIEJSZYM zbiorze
    // i sprawdzamy Contains() w WIĘKSZYM (O(1) dla HashSet).
    // Jeden przebieg, zero kopii HashSetu, zwracamy gotowy intersection
    // który potem użyje ZnajdzPodobneZakresy (zamiast liczyć go po raz 3.).
    static (HashSet<string> intersection, double jaccard, double aDoB, double bDoA)
        ObliczStatystyki(HashSet<string> A, HashSet<string> B)
    {
        if (A.Count == 0 || B.Count == 0)
            return (new HashSet<string>(), 0, 0, 0);

        // iterujemy po mniejszym
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