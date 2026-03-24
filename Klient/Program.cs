using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

class Client
{
    static List<string> adresySerwera = new List<string>
    {
        "127.0.0.1"
        // "192.168.1.x" // drugi komputer
        // "192.168.1.y" // trzeci komputer
    };
    static int port = 8001;
    static int n = 4;
    static int grainSize = 1;
    static double prog = 30.0;

    static void Main(string[] args)
    {
        //tu mozesz przetestiwac te funkcje dobierania par, dziala z tego co widze
       /* Console.WriteLine("Testuje generowanie par...");
        var paryTest = GenerujPary(new List<string> { "a.txt", "b.txt", "c.txt" });
        Console.WriteLine("Wygenerowane pary:");
        foreach (var (a, b) in paryTest)
        {
            Console.WriteLine($"{a} vs {b}");
        }
        */
        // na razie wpisujemy teksty z klawiatury
        Console.WriteLine("Wpisz pierwszy tekst i nacisnij Enter:");
        string tekst1 = Console.ReadLine();

        Console.WriteLine("Wpisz drugi tekst i nacisnij Enter:");
        string tekst2 = Console.ReadLine();

        // wysylamy do serwera i odbieramy wynik
        WyslijTeksty(adresySerwera[0], tekst1, tekst2);
        Koniec();

        // pozniej tutaj bedzie:
        // List<string> pliki = WczytajIposortuj(args);
        // List<(string, string)> pary = GenerujPary(pliki);
        // List<(string, string, double)> wyniki = RozdzielZadania(pary, pliki);
        // WyswietlMacierz(wyniki, pliki);
    }

    // na razie wysyla dwa teksty i odbiera liczbe slow
    // pozniej zastapiona przez WyslijZadanie()

    static void Koniec()//Pomyślałem ze dodam takie cos co wrzucimy na koncu poki co, bo jak klient dostaje odpowiedz to znika konsola, wiec
        //poki co zeby mozna bylo zobacz wynikiXD pozdro
    {
        Console.WriteLine("Naciśnij dowolny klawisz, aby zakończyć...");
        Console.ReadKey(); // czeka aż użytkownik wciśnie **dowolny klawisz**
    }
    static void WyslijTeksty(string adres, string tekst1, string tekst2)
    {
        try
        {
            Console.WriteLine("[KLIENT] Lacze sie z serwerem " + adres + ":" + port + "...");
            TcpClient klient = new TcpClient(adres, port);
            Console.WriteLine("[KLIENT] Polaczono!");
            Thread.Sleep(500);

            NetworkStream stream = klient.GetStream();
            BinaryReader reader = new BinaryReader(stream);
            BinaryWriter writer = new BinaryWriter(stream);

            Console.WriteLine("[KLIENT] Wysylam parametry (n=" + n + " grainSize=" + grainSize + ")...");
            writer.Write(n);
            writer.Write(grainSize);
            Thread.Sleep(500);

            Console.WriteLine("[KLIENT] Wysylam tekst 1...");
            writer.Write(tekst1);
            Thread.Sleep(500);

            Console.WriteLine("[KLIENT] Wysylam tekst 2...");
            writer.Write(tekst2);
            Thread.Sleep(500);

            Console.WriteLine("[KLIENT] Czekam na wyniki od serwera...");
            int iloscSlow1 = reader.ReadInt32();
            int iloscSlow2 = reader.ReadInt32();

            Console.WriteLine("[KLIENT] Otrzymalem wyniki!");
            Console.WriteLine("[KLIENT] Tekst 1 ma slow: " + iloscSlow1);
            Console.WriteLine("[KLIENT] Tekst 2 ma slow: " + iloscSlow2);

            klient.Close();
        }
        catch (Exception e)
        {
            Console.WriteLine("Blad: " + e.Message);
        }
    }

    // wysyla jeden plik przez siec
    static void WyslijPlik(BinaryWriter writer, string sciezka)
    {
        byte[] dane = File.ReadAllBytes(sciezka);
        string nazwa = System.IO.Path.GetFileName(sciezka);

        writer.Write(nazwa);
        writer.Write((long)dane.Length);
        writer.Write(dane);
    }

    // sprawdzic czy kazdy plik istnieje
    // posortowac po File.ReadAllBytes(p).Length malejaco
    // wczytuje pliki podane jako argumenty i sortuje wg rozmiaru
    static List<string> WczytajIposortuj(string[] args)
    {
        List<string> istniejącePliki = new List<string>();

        foreach (string sciezka in args)
        {
            if (File.Exists(sciezka))
            {
                istniejącePliki.Add(sciezka);
            }
            else
            {
                Console.WriteLine("[OSTRZEŻENIE] Plik nie istnieje: " + sciezka);
            }
        }

        istniejącePliki.Sort((a, b) =>
        {
            long rozmiarA = new FileInfo(a).Length;
            long rozmiarB = new FileInfo(b).Length;
            return rozmiarB.CompareTo(rozmiarA); // malejąco
        });

        return istniejącePliki;
    }
    //generuje liste wszystkich unikalnych par plikow
    static List<(string, string)> GenerujPary(List<string> pliki)
    {
        List<(string, string)> pary = new List<(string, string)>();

        for (int i = 0; i < pliki.Count; i++)
        {
            for (int j = i + 1; j < pliki.Count; j++)
            {
                pary.Add((pliki[i], pliki[j]));
            }
        }

        return pary;
    }

    // rozdziela pary miedzy dostepne serwery
    static List<(string, string, double)> RozdzielZadania(List<(string, string)> pary, List<string> pliki)
    {
        // TODO: podzielic pary na paczki wg grainSize
        // TODO: przydzielic paczki do serwerow round-robin
        // TODO: wyslac zadania wspolbieznie
        // TODO: zebrac wyniki i zwrocic
        return new List<(string, string, double)>();
    }

    // wysyla dwa pliki do serwera i odbiera wynik podobienstwa
    static (string, string, double) WyslijZadanie(string adres, string plikA, string plikB)
    {
        try
        {
            TcpClient klient = new TcpClient(adres, port);
            NetworkStream stream = klient.GetStream();
            BinaryReader reader = new BinaryReader(stream);
            BinaryWriter writer = new BinaryWriter(stream);

            writer.Write(n);
            writer.Write(grainSize);

            WyslijPlik(writer, plikA);
            WyslijPlik(writer, plikB);

            string nazwaA = reader.ReadString();
            string nazwaB = reader.ReadString();
            double podobienstwo = reader.ReadDouble();

            klient.Close();
            return (nazwaA, nazwaB, podobienstwo);
        }
        catch (Exception e)
        {
            Console.WriteLine("Blad polaczenia z " + adres + ": " + e.Message);
            // TODO: ponowna proba z innym serwerem
            return (plikA, plikB, -1);
        }
    }

    // wyswietla wyniki w konsoli
    static void WyswietlMacierz(List<(string, string, double)> wyniki, List<string> pliki)
    {
        // TODO: wyswietlic naglowki kolumn
        // TODO: dla kazdej pary znalezc wynik i wyswietlic
        // TODO: oznaczyc pary powyzej progu jako [PLAGIAT?]
        Console.WriteLine("\n=== WYNIKI ===");
        foreach (var (a, b, podobienstwo) in wyniki)
        {
            string status = podobienstwo >= prog ? " [PLAGIAT?]" : "";
            Console.WriteLine(a + " vs " + b + " = " + podobienstwo + "%" + status);
        }
    }

    // zapisuje wyniki do pliku CSV
    static void EksportujCSV(List<(string, string, double)> wyniki, string sciezkaWyjsciowa)
    {
        // TODO: StreamWriter, zapisac naglowek i kolejne wiersze
    }
}