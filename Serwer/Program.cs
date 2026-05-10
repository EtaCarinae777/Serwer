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
using static System.Net.Mime.MediaTypeNames;
using Newtonsoft.Json;

class Server
{
    static int port = 8001;

    // cache przechowuje juz obliczone ngramy dla kazdego pliku
    // klucz = nazwa pliku + rozmiar tekstu + n
    static Dictionary<string, (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)> cache
        = new Dictionary<string, (HashSet<string>, Dictionary<string, (int, int)>)>();

    // lock do synchronizacji dostepu do cache z wielu watkow
    static readonly object cacheLock = new object();

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
        byte[] dane = reader.ReadBytes((int)rozmiar);

        return (nazwa, System.Text.Encoding.UTF8.GetString(dane));
    }

    static void ObsluzKlienta(TcpClient klient)
    {
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

                // pobieramy ngramy z cache lub liczymy jesli pierwsza wizyta
                // oba pliki tokenizowane rownoleglie
                var task1 = Task.Run(() => PobierzNgramy(nazwa1, tekst1, n));
                var task2 = Task.Run(() => PobierzNgramy(nazwa2, tekst2, n));
                Task.WaitAll(task1, task2);

                var (ngramy1, pozycje1) = task1.Result;
                var (ngramy2, pozycje2) = task2.Result;

                //intersection - czesc wspolna n-gramow
                var intersection = new HashSet<string>(ngramy1);
                intersection.IntersectWith(ngramy2);

                //jaccard - podobienstwo w procentach
                double Jaccard = ObliczJaccard(ngramy1, ngramy2);

                //dwustronne podobienstwo
                var (aDoB, bDoA) = ObliczDwustronne(ngramy1, ngramy2);

                // zakresy podobnych fragmentow
                var zakresy1 = ZnajdzPodobneZakresy(pozycje1, intersection);
                var zakresy2 = ZnajdzPodobneZakresy(pozycje2, intersection);

                stoper.Stop();
                long czasObliczenMs = stoper.ElapsedMilliseconds;

                string csvContent = GenerujCSV(nazwa1, nazwa2, Jaccard, aDoB, bDoA, zakresy1, zakresy2);

                // wysylamy wyniki
                writer.Write(Jaccard);
                writer.Write(aDoB);
                writer.Write(bDoA);

                // wysylamy zakresy pliku 1
                writer.Write(zakresy1.Count);
                foreach (var (od, do_) in zakresy1)
                {
                    writer.Write(od);
                    writer.Write(do_);
                }

                // wysylamy zakresy pliku 2
                writer.Write(zakresy2.Count);
                foreach (var (od, do_) in zakresy2)
                {
                    writer.Write(od);
                    writer.Write(do_);
                }

                string jsonContent = GenerujJSON(
                    nazwa1, nazwa2,
                    tekst1, tekst2,
                    Jaccard, aDoB, bDoA,
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
    }

    // pobiera ngramy z cache lub oblicza je jesli jeszcze nie ma
    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)
        PobierzNgramy(string nazwaPliku, string tekst, int n)
    {
        // klucz = nazwa pliku + rozmiar tekstu + n
        // jesli plik sie zmienil (inny rozmiar) liczymy od nowa
        string klucz = nazwaPliku + "_" + tekst.Length + "_n" + n;

        lock (cacheLock)
        {
            if (cache.ContainsKey(klucz))
                return cache[klucz];
        }

        // nie ma w cache - liczymy
        var tokeny = PodzielNaSlowa(tekst);
        var wynik = GenerujNgramy(tokeny, n);

        lock (cacheLock)
        {
            if (!cache.ContainsKey(klucz))
                cache[klucz] = wynik;
        }

        return wynik;
    }

    // na razie liczy slowa, pozniej zastapiona przez PorownajPliki()
    static int LiczSlowa(string tekst)
    {
        string[] slowa = tekst.Split(new char[] { ' ', '\t', '\n', '\r' },
                                     StringSplitOptions.RemoveEmptyEntries);
        return slowa.Length;
    }

    // glowna funkcja porownujaca dwa teksty
    // zwraca podobienstwo w procentach (0.0 - 100.0)
    static double PorownajPliki(string tekstA, string tekstB, int n)
    {
        // TODO: wywolac GenerujNgramy dla obu tekstow
        // TODO: wywolac ObliczJaccard na zbiorach n-gramow
        return 0.0;
    }

    // dzieli tekst na tokeny zachowujac pozycje w oryginalnym tekscie
    // zwraca liste (slowo, pozycja_poczatku, pozycja_konca)
    static List<(string slowo, int od, int do_)> PodzielNaSlowa(string tekst)
    {
        var tokeny = new List<(string slowo, int od, int do_)>();
        int i = 0;

        while (i < tekst.Length)
        {
            // pomijamy znaki niebedace literami ani cyframi
            if (!char.IsLetterOrDigit(tekst[i]))
            {
                i++;
                continue;
            }

            // znalezlismy poczatek slowa
            int poczatek = i;

            // idziemy do konca slowa
            while (i < tekst.Length && char.IsLetterOrDigit(tekst[i]))
                i++;

            // zapisujemy slowo z pozycjami w oryginalnym tekscie
            string slowo = tekst.Substring(poczatek, i - poczatek).ToLower();
            tokeny.Add((slowo, poczatek, i));
        }

        return tokeny;
    }

    // generuje n-gramy z tokenow
    // zwraca zbior n-gramow (do Jaccarda)
    // oraz slownik ngram -> (od, do) potrzebny do podswietlania
    static (HashSet<string> ngramy, Dictionary<string, (int od, int do_)> pozycje)
        GenerujNgramy(List<(string slowo, int od, int do_)> tokeny, int n)
    {
        var ngramy = new HashSet<string>();
        var pozycje = new Dictionary<string, (int od, int do_)>();

        for (int i = 0; i <= tokeny.Count - n; i++)
        {
            // laczmy n slow w jeden ngram
            string ngram = string.Join(" ", tokeny.GetRange(i, n).Select(t => t.slowo));
            ngramy.Add(ngram);

            // zapamietujemy pozycje pierwszego i ostatniego slowa w oryginalnym tekscie
            if (!pozycje.ContainsKey(ngram))
                pozycje[ngram] = (tokeny[i].od, tokeny[i + n - 1].do_);
        }

        return (ngramy, pozycje);
    }

    // oblicza wspolczynnik Jaccarda dla dwoch zbiorow n-gramow
    static double ObliczJaccard(HashSet<string> ngramyA, HashSet<string> ngramyB)
    {
        // TODO: obliczyc czesc wspolna (intersection)
        var intersection = new HashSet<string>(ngramyA);
        intersection.IntersectWith(ngramyB);
        // TODO: obliczyc sume zbiorow (union)
        var union = new HashSet<string>(ngramyA);
        union.UnionWith(ngramyB);
        // TODO: podzielic i zwrocic wynik * 100
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

    // zamiast zdan zwraca liste zakresow (od, do) w oryginalnym tekscie
    // dzieki temu GUI moze bezposrednio podswietlic fragmenty
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

        // sortujemy po pozycji poczatku
        zakresy.Sort((a, b) => a.od.CompareTo(b.od));

        // laczymy zakresy ktore sie nakladaja lub stykaja
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

    static string GenerujJSON(
        string nazwaA,
        string nazwaB,
        string tekstA,
        string tekstB,
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
            tekstA = tekstA,
            tekstB = tekstB,
            zakresy1 = zakresy1.Select(z => new { od = z.od, do_ = z.do_ }).ToList(),
            zakresy2 = zakresy2.Select(z => new { od = z.od, do_ = z.do_ }).ToList()
        };

        return Newtonsoft.Json.JsonConvert.SerializeObject(dane, Newtonsoft.Json.Formatting.Indented);
    }

    // sprawdza czy wynik przekracza prog plagiatu
    static bool CzyPlagiat(double podobienstwo, double prog)
    {
        // TODO: prosta comparacja podobienstwo >= prog
        return false;
    }
}