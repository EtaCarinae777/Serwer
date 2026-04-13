using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

class Client
{
    static List<string> adresySerwera = new List<string>
    {
        "192.168.0.30",
        "192.168.0.32"
        
    };
    static int port = 8001;
    static int n = 4;
    static int grainSize = 1;
    static double prog = 30.0;

    static void Main(string[] args)
    {
        List<string> posortowane = WczytajIposortuj(PobierzSciezkiOdUzytkownika());
        List<(string, string)> pary = GenerujPary(posortowane);
        WyswietlPary(pary);

        var wyniki = RozdzielZadania(pary, posortowane);
        WyswietlMacierz(wyniki, posortowane);

        // pozniej tutaj bedzie:
        // List<(string, string, double)> wyniki = RozdzielZadania(pary, posortowane);
        // WyswietlMacierz(wyniki, posortowane);
    }

    // pyta uzytkownika o sciezki do plikow
    // konczy gdy uzytkownik wcisnnie pusty Enter
    static string[] PobierzSciezkiOdUzytkownika()
    {
        Console.WriteLine("Podaj sciezki do plikow (pusty Enter aby zakonczyc):");
        List<string> sciezki = new List<string>();

        while (true)
        {
            string linia = Console.ReadLine();
            if (linia == "") break;
            sciezki.Add(linia);
        }

        if (sciezki.Count < 2)
        {
            Console.WriteLine("Podaj co najmniej dwa pliki!");
            return new string[0];
        }

        return sciezki.ToArray();
    }

    // wczytuje pliki i sortuje malejaco wg rozmiaru
    static List<string> WczytajIposortuj(string[] args)
    {
        List<string> pliki = new List<string>();

        foreach (string sciezka in args)
        {
            if (!File.Exists(sciezka))
            {
                Console.WriteLine("Nie ma takiego pliku: " + sciezka);
                continue;
            }
            pliki.Add(sciezka);
        }

        // sortujemy malejaco - najwieksze pliki pierwsze
        pliki.Sort((a, b) => new FileInfo(b).Length.CompareTo(new FileInfo(a).Length));

        Console.WriteLine("\n=== PLIKI PO POSORTOWANIU ===");
        foreach (string plik in pliki)
        {
            long rozmiar = new FileInfo(plik).Length;
            Console.WriteLine(Path.GetFileName(plik) + " - " + rozmiar + " bajtow");
        }

        return pliki;
    }

    // generuje liste wszystkich unikalnych par plikow
    static List<(string, string)> GenerujPary(List<string> pliki)
    {
        List<(string, string)> pary = new List<(string, string)>();

        // podwojna petla - kazda para tylko raz
        for (int i = 0; i < pliki.Count; i++)
        {
            for (int j = i + 1; j < pliki.Count; j++)
            {
                pary.Add((pliki[i], pliki[j]));
            }
        }

        return pary;
    }

    // wyswietla wygenerowane pary z rozmiarami plikow
    static void WyswietlPary(List<(string, string)> pary)
    {
        Console.WriteLine("\n=== PARY DO POROWNANIA ===");
        foreach (var (a, b) in pary)
        {
            long rozmiarA = new FileInfo(a).Length;
            long rozmiarB = new FileInfo(b).Length;
            Console.WriteLine(Path.GetFileName(a) + " (" + rozmiarA + " B) vs "
                            + Path.GetFileName(b) + " (" + rozmiarB + " B)");
        }
        Console.WriteLine("Lacznie par: " + pary.Count);
    }

    

    // wysyla jeden plik przez siec
    static void WyslijPlik(BinaryWriter writer, string sciezka)
    {
        byte[] dane = File.ReadAllBytes(sciezka);
        string nazwa = Path.GetFileName(sciezka);

        writer.Write(nazwa);              // Nazwa pliku
        writer.Write((long)dane.Length);  // Rozmiar pliku
        writer.Write(dane);               // Dane pliku
    }

    static void WyslijParyPlikow(string adres, string sciezka1, string sciezka2)
    {
        try
        {
            Console.WriteLine($"[KLIENT] Lacze sie z serwerem {adres}:{port}...");

            using (TcpClient klient = new TcpClient(adres, port))
            using (NetworkStream stream = klient.GetStream())
            using (BinaryReader reader = new BinaryReader(stream))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                Console.WriteLine("[KLIENT] Polaczono!");

                Console.WriteLine($"[KLIENT] Wysylam parametry (n={n} grainSize={grainSize})...");
                writer.Write(n);
                writer.Write(grainSize);

                Console.WriteLine("[KLIENT] Wysylam plik 1...");
                WyslijPlik(writer, sciezka1);

                Console.WriteLine("[KLIENT] Wysylam plik 2...");
                WyslijPlik(writer, sciezka2);

                writer.Flush();

                //odebranie wyniku zaka :DD
                Console.WriteLine("[KLIENT] Czekam na wyniki od serwera...");
                double jaccard = reader.ReadDouble();
                double aDoB = reader.ReadDouble();
                double bDoA = reader.ReadDouble();
                int liczbaPodobnychZdan1 = reader.ReadInt32();
                int liczbaPodobnychZdan2 = reader.ReadInt32();

                Console.WriteLine("\n=== WYNIK POROWNANIA ===");
                Console.WriteLine("Plik 1: " + Path.GetFileName(sciezka1));
                Console.WriteLine("Plik 2: " + Path.GetFileName(sciezka2));
                Console.WriteLine("Podobienstwo Jaccarda:     " + jaccard.ToString("F2") + "%");
                Console.WriteLine("Plik 1 zawarty w pliku 2:  " + aDoB.ToString("F2") + "%");
                Console.WriteLine("Plik 2 zawarty w pliku 1:  " + bDoA.ToString("F2") + "%");
                Console.WriteLine("Podobne zdania w pliku 1:  " + liczbaPodobnychZdan1);
                Console.WriteLine("Podobne zdania w pliku 2:  " + liczbaPodobnychZdan2);

                Console.WriteLine("[KLIENT] Otrzymalem wynik!");
                Console.WriteLine("[KLIENT] Podobienstwo: " + jaccard.ToString("F2") + "%");
            }
        }
        catch (Exception e)
        {
            Console.WriteLine($"[KLIENT] Blad: {e.Message}");
        }
    }

    // rozdziela pary miedzy dostepne serwery
    static List<(string, string, double)> RozdzielZadania(List<(string, string)> pary, List<string> pliki)
    {
        List<Task<(string, string, double)>> taski = new List<Task<(string, string, double)>>();

        for (int i = 0; i < pary.Count; i++)
        {
            var para = pary[i];
            string adres = adresySerwera[i % adresySerwera.Count];

            var task = Task.Run(() =>
            {
                return WyslijZadanie(adres, para.Item1, para.Item2);
            });

            taski.Add(task);
        }

        Task.WaitAll(taski.ToArray());

        return taski.Select(t => t.Result).ToList();
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

            writer.Flush();

            double jaccard = reader.ReadDouble();
            double aDoB = reader.ReadDouble();
            double bDoA = reader.ReadDouble();
            int liczba1 = reader.ReadInt32();
            int liczba2 = reader.ReadInt32();

            klient.Close();

            return (plikA, plikB, jaccard);
        }
        catch (Exception e)
        {
            Console.WriteLine("Blad polaczenia z " + adres + ": " + e.Message);
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