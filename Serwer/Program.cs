using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

class Server
{
    static int port = 8001;

    static void Main()
    {
        TcpListener serwer = new TcpListener(IPAddress.Any, port);
        serwer.Start();

        while (true)
        {
            TcpClient klient = serwer.AcceptTcpClient();
            Console.WriteLine("Nowe polaczenie od klienta");
            Task.Run(() => ObsluzKlienta(klient));
        }
    }

    static (string nazwa, string tekst) OdbierzPlik(BinaryReader reader)
    {
        string nazwa = reader.ReadString();
        long rozmiar = reader.ReadInt64();
        byte[] dane = reader.ReadBytes((int)rozmiar);

        Console.WriteLine($"[SERWER] Odebrano plik: {nazwa} ({rozmiar} bajtow)");
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
                Console.WriteLine("[SERWER] Klient polaczony!");

                // Odbierz parametry
                int n = reader.ReadInt32();
                int grainSize = reader.ReadInt32();
                Console.WriteLine($"[SERWER] Parametry: n={n}, grainSize={grainSize}");

                // Odbierz plik 1
                Console.WriteLine("[SERWER] Odbieram plik 1...");
                var (nazwa1, tekst1) = OdbierzPlik(reader);

                // Odbierz plik 2
                Console.WriteLine("[SERWER] Odbieram plik 2...");
                var (nazwa2, tekst2) = OdbierzPlik(reader);

                Console.WriteLine("[SERWER] Otrzymałem parę. Przystępuje do generowania i wypisania ngramów");

                //1. generowanie n-gramów dla obu tekstów
                HashSet<string> ngramy1 = GenerujNgramy(tekst1, n);
                HashSet<string> ngramy2 = GenerujNgramy(tekst2, n);

                //intersection - czesc wspolna n-gramow
                var intersection = new HashSet<string>(ngramy1);
                intersection.IntersectWith(ngramy2);

                //jaccard - podobienstwo w procentach
                double Jaccard = ObliczJaccard(ngramy1, ngramy2);
                Console.WriteLine($"Współczynnik Jaccarda: {Jaccard}%");

                //dwustronne podobienstwo
                var (aDoB, bDoA) = ObliczDwustronne(ngramy1, ngramy2);

                //zdania z podobnymi fragmentami
                var zdania1 = ZnajdzPodobneZdania(tekst1, intersection, n);
                var zdania2 = ZnajdzPodobneZdania(tekst2, intersection, n);

                // zapis do CSV
                string csvContent = GenerujCSV(
                    nazwa1, nazwa2,
                    Jaccard, aDoB, bDoA,
                    zdania1, zdania2
                    );

                //wysylanie wyniku do klienta, bo generowalo blad przy braku
                writer.Write(Jaccard);
                writer.Write(aDoB);
                writer.Write(bDoA);
                writer.Write(zdania1.Count);
                writer.Write(zdania2.Count);
                writer.Write(csvContent);

                Console.WriteLine("[SERWER] Zakonczono!");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[SERWER] Blad: {e.Message}");
        }
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

    // generuje zbior n-gramow dla podanego tekstu
    static HashSet<string> GenerujNgramy(string tekst, int n)
    {
        List<String> slowa = PodzielNaSlowa(tekst);
        HashSet<string> ngramy = new HashSet<string>();

        for (int i = 0; i <= slowa.Count - n; i++)
        {
            string ngram = string.Join(" ", slowa.GetRange(i, n));
            ngramy.Add(ngram);
        }
        // TODO: kazdy n-gram dodac do HashSet
        return ngramy;
    }

    //dzielimy tekst na slowa
    static List<string> PodzielNaSlowa(string tekst)
    {
        tekst = tekst.ToLower();
        tekst = Regex.Replace(tekst, @"[^\p{L}\p{Nd}\s]+", "");
        return tekst
            .Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
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

    static List<string> PodzielNaZdania(string tekst)
    {
        return Regex.Split(tekst, @"(?<=[\.!\?])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    static List<string> ZnajdzPodobneZdania(string tekst, HashSet<string> wspolne, int n)
    {
        var zdania = PodzielNaZdania(tekst);
        var wynik = new List<string>();

        foreach (var zdanie in zdania)
        {
            var ngramy = GenerujNgramy(zdanie, n);

            foreach (var ng in ngramy)
            {
                if (wspolne.Contains(ng))
                {
                    wynik.Add(zdanie);
                    break;
                }
            }
        }

        return wynik.Distinct().ToList();
    }

    static string GenerujCSV(
    string nazwaA,
    string nazwaB,
    double jaccard,
    double aDoB,
    double bDoA,
    List<string> zdania1,
    List<string> zdania2)
    {
        StringBuilder sw = new StringBuilder();

        string fileA = Path.GetFileNameWithoutExtension(nazwaA);
        string fileB = Path.GetFileNameWithoutExtension(nazwaB);

        sw.AppendLine("PlikA,PlikB,Jaccard,A->B,B->A,Typ,Dane");

        string jaccardStr = jaccard.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        string aDoBStr = aDoB.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        string bDoAStr = bDoA.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);

        sw.AppendLine($"{fileA},{fileB},{jaccardStr},{aDoBStr},{bDoAStr},STATYSTYKA,-");

        foreach (var z in zdania1)
            sw.AppendLine($"{fileA},{fileB},,,,ZDANIE_A,\"{z}\"");

        foreach (var z in zdania2)
            sw.AppendLine($"{fileA},{fileB},,,,ZDANIE_B,\"{z}\"");

        return sw.ToString();
    }

    // sprawdza czy wynik przekracza prog plagiatu
    static bool CzyPlagiat(double podobienstwo, double prog)
    {
        // TODO: prosta comparacja podobienstwo >= prog
        return false;
    }
}