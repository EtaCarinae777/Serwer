using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;

class Client
{
    static List<string> adresySerwera = new List<string>
    {
        //"192.168.0.30",
        "192.168.0.32",
        "192.168.0.13"

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
                string csv = reader.ReadString(); 

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
            // i % adresySerwera.Count sugeruje startowy serwer, 
            // aby nie wszystkie zadania uderzały w ten sam serwer na starcie
            int startowyIndeks = i % adresySerwera.Count;

            var task = Task.Run(() =>
            {
                return WyslijZadanie_Failover(startowyIndeks, para.Item1, para.Item2);
            });

            taski.Add(task);
        }

        Task.WaitAll(taski.ToArray());
        return taski.Select(t => t.Result).ToList();
    }
    static (string, string, double) WyslijZadanie_Failover(int startIdx, string plikA, string plikB)
    {
        // Próbujemy po kolei każdego serwera z listy
        for (int i = 0; i < adresySerwera.Count; i++)
        {
            int aktualnyIndeks = (startIdx + i) % adresySerwera.Count;
            string adres = adresySerwera[aktualnyIndeks];

            // Wykorzystujemy Twoją istniejącą funkcję WyslijZadanie
            var wynik = WyslijZadanie(adres, plikA, plikB);

            // Jeśli wynik jest poprawny (nie -1), zwracamy go i kończymy próby dla tej pary
            if (wynik.Item3 >= 0)
            {
                return wynik;
            }

            Console.WriteLine($"[FAILOVER] Serwer {adres} zawiódł dla pary {Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)}. Próbuję następny...");
        }

        // Jeśli pętla się skończyła i żaden serwer nie odpowiedział
        return (plikA, plikB, -1.0);
    }

    // wysyla dwa pliki do serwera i odbiera wynik podobienstwa
    static (string, string, double) WyslijZadanie(string adres, string plikA, string plikB)
    {
        // Klauzula using automatycznie zamknie połączenie, nawet jak wystąpi błąd
        try
        {
            using (TcpClient klient = new TcpClient())
            {
                // Ustawiamy krótkie timeouty (np. 5 sekund)
                klient.ReceiveTimeout = 5000;
                klient.SendTimeout = 5000;

                // Próba połączenia
                klient.Connect(adres, port);

                using (NetworkStream stream = klient.GetStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    // Wysyłanie parametrów
                    writer.Write(n);
                    writer.Write(grainSize);

                    // Wysyłanie plików
                    WyslijPlik(writer, plikA);
                    WyslijPlik(writer, plikB);
                    writer.Flush();

                    // Odbieranie danych - DODAJ TRY-CATCH tutaj, jeśli serwer może wysłać śmieci
                    // Musimy odczytać WSZYSTKO co serwer wysłał, 
                    // żeby nie zostawić śmieci w buforze (nawet jeśli nie używasz tych zmiennych)
                    double jaccard = reader.ReadDouble();
                    double aDoB = reader.ReadDouble();
                    double bDoA = reader.ReadDouble();
                    int liczba1 = reader.ReadInt32();
                    int liczba2 = reader.ReadInt32();
                    string csv = reader.ReadString();
                    
                    ZapiszCSVLokalnie(plikA, plikB, csv);
                    return (plikA, plikB, jaccard);
                }
            }
        }
        catch (SocketException ex)
        {
            Console.WriteLine($"[BŁĄD SIECI] {adres}: Serwer jest wyłączony lub port zablokowany.");
            return (plikA, plikB, -1);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[BŁĄD TRANSMISJI] {adres}: Serwer zerwał połączenie w trakcie wysyłania.");
            return (plikA, plikB, -1);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BŁĄD NIEOCZEKIWANY] {adres}: {ex.Message}");
            return (plikA, plikB, -1);
        }
    }

    static void ZapiszCSVLokalnie(string plikA, string plikB, string csv)
    {
        string folder = "Raporty";
        Directory.CreateDirectory(folder);

        string nameA = Path.GetFileNameWithoutExtension(plikA);
        string nameB = Path.GetFileNameWithoutExtension(plikB);

        string path = Path.Combine(folder,
            $"{nameA}_VS_{nameB}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

        File.WriteAllText(path, csv);

        Console.WriteLine($"[KLIENT] Zapisano raport: {path}");
    }

    // wyswietla wyniki w konsoli
    static void WyswietlMacierz(List<(string, string, double)> wyniki, List<string> pliki)
    {
        Console.WriteLine("\n=== WYNIKI ===");
        foreach (var (a, b, podobienstwo) in wyniki)
        {
            string nazwaA = Path.GetFileName(a);
            string nazwaB = Path.GetFileName(b);

            // 1. Obsługa tekstu wyniku (procent lub błąd)
            string wynikTekst = podobienstwo < 0 ? "BŁĄD POŁĄCZENIA" : $"{podobienstwo:F2}%";

            // 2. Logika statusu plagiatu (tylko jeśli nie ma błędu)
            string status = (podobienstwo >= prog && podobienstwo >= 0) ? " [PLAGIAT?]" : "";

            // 3. Wyświetlanie z opcjonalnym kolorem dla błędów
            if (podobienstwo < 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{nazwaA} vs {nazwaB} = {wynikTekst}");
                Console.ResetColor();
            }
            else
            {
                Console.WriteLine($"{nazwaA} vs {nazwaB} = {wynikTekst}{status}");
            }
        }
    }

    // zapisuje wyniki do pliku CSV
    static void EksportujCSV(List<(string, string, double)> wyniki, string sciezkaWyjsciowa)
    {
        // TODO: StreamWriter, zapisac naglowek i kolejne wiersze
    }
}