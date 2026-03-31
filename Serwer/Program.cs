using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
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

    static void OdbierzTeksty(TcpClient klient)
    {
        NetworkStream stream = klient.GetStream();
        BinaryReader reader = new BinaryReader(stream);
        BinaryWriter writer = new BinaryWriter(stream);

        Console.WriteLine("[SERWER] Czekam na parametry...");
        int n = reader.ReadInt32();
        int grainSize = reader.ReadInt32();
        Console.WriteLine("[SERWER] Otrzymalem parametry: n=" + n + " grainSize=" + grainSize);
        Thread.Sleep(1000);

        Console.WriteLine("[SERWER] Czekam na tekst 1...");
        string tekst1 = reader.ReadString();
        Console.WriteLine("[SERWER] Otrzymalem tekst 1: " + tekst1);
        Thread.Sleep(1500);

        Console.WriteLine("[SERWER] Czekam na tekst 2...");
        string tekst2 = reader.ReadString();
        Console.WriteLine("[SERWER] Otrzymalem tekst 2: " + tekst2);
        Thread.Sleep(2000);

        Console.WriteLine("[SERWER] Licze slowa...");
        Thread.Sleep(1000);
        int iloscSlow1 = LiczSlowa(tekst1);
        int iloscSlow2 = LiczSlowa(tekst2);

        Console.WriteLine("[SERWER] Wyniki: tekst1=" + iloscSlow1 + " slow, tekst2=" + iloscSlow2 + " slow");
        Thread.Sleep(500);

        Console.WriteLine("[SERWER] Odsylam wyniki do klienta...");
        writer.Write(iloscSlow1);
        writer.Write(iloscSlow2);
        Console.WriteLine("[SERWER] Gotowe!");

        klient.Close();

    }
    static string OdbierzPlik(BinaryReader reader)
    {
        string nazwa = reader.ReadString();
        long rozmiar = reader.ReadInt64();
        byte[] dane = reader.ReadBytes((int)rozmiar);

        Console.WriteLine($"[SERWER] Odebrano plik: {nazwa} ({rozmiar} bajtow)");
        return System.Text.Encoding.UTF8.GetString(dane);
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
                string tekst1 = OdbierzPlik(reader);

                // Odbierz plik 2
                Console.WriteLine("[SERWER] Odbieram plik 2...");
                string tekst2 = OdbierzPlik(reader);

                Console.WriteLine("[SERWER] Otrzymałem parę. Przystępuje do generowania i wypisania ngramów");

                HashSet<string> ngramy1 = GenerujNgramy(tekst1, n);
                HashSet<string> ngramy2 = GenerujNgramy(tekst2, n);
                
                double Jaccard = ObliczJaccard(ngramy1, ngramy2);
                Console.WriteLine($"Współczynnik Jaccarda: {Jaccard}%");
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
        return 0.0;
    }

    // sprawdza czy wynik przekracza prog plagiatu
    static bool CzyPlagiat(double podobienstwo, double prog)
    {
        // TODO: prosta comparacja podobienstwo >= prog
        return false;
    }
}