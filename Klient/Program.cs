using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

class Client
{
    static List<string> adresySerwera = new List<string>
    {
        "127.0.0.1",
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

        // mierzymy czas calej analizy
        var stoperCalkowity = System.Diagnostics.Stopwatch.StartNew();

        var wyniki = RozdzielZadania(pary, posortowane);

        stoperCalkowity.Stop();
        long czasCalkowitejAnalizy = stoperCalkowity.ElapsedMilliseconds;

        WyswietlMacierz(wyniki, posortowane);
        WyswietlPodsumowanie(wyniki, pary.Count, czasCalkowitejAnalizy);
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

                // odbieramy zakresy pliku 1
                int liczbaZakresow1 = reader.ReadInt32();
                var zakresy1 = new List<(int od, int do_)>();
                for (int i = 0; i < liczbaZakresow1; i++)
                    zakresy1.Add((reader.ReadInt32(), reader.ReadInt32()));

                // odbieramy zakresy pliku 2
                int liczbaZakresow2 = reader.ReadInt32();
                var zakresy2 = new List<(int od, int do_)>();
                for (int i = 0; i < liczbaZakresow2; i++)
                    zakresy2.Add((reader.ReadInt32(), reader.ReadInt32()));

                string csv = reader.ReadString();

                Console.WriteLine("\n=== WYNIK POROWNANIA ===");
                Console.WriteLine("Plik 1: " + Path.GetFileName(sciezka1));
                Console.WriteLine("Plik 2: " + Path.GetFileName(sciezka2));
                Console.WriteLine("Podobienstwo Jaccarda:     " + jaccard.ToString("F2") + "%");
                Console.WriteLine("Plik 1 zawarty w pliku 2:  " + aDoB.ToString("F2") + "%");
                Console.WriteLine("Plik 2 zawarty w pliku 1:  " + bDoA.ToString("F2") + "%");
                Console.WriteLine("Podobne fragmenty w pliku 1:  " + liczbaZakresow1);
                Console.WriteLine("Podobne fragmenty w pliku 2:  " + liczbaZakresow2);
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
        // wrzucamy wszystkie pary do kolejki
        var kolejka = new System.Collections.Concurrent.ConcurrentQueue<(string, string)>();
        foreach (var para in pary)
            kolejka.Enqueue(para);

        // lista na wyniki - ConcurrentBag bo wiele watkow bedzie dodawac wyniki jednoczesnie
        var wyniki = new System.Collections.Concurrent.ConcurrentBag<(string, string, double)>();

        // tworzymy tyle watkow ile mamy serwerow
        List<Task> taski = new List<Task>();

        for (int i = 0; i < adresySerwera.Count; i++)
        {
            int indeksSerwera = i;

            var task = Task.Run(() =>
            {
                // kazdy watek pobiera pary z kolejki dopoki sa dostepne
                while (kolejka.TryDequeue(out var para))
                {
                    Console.WriteLine($"[KLIENT] Serwer {adresySerwera[indeksSerwera]} pobral pare: " +
                        Path.GetFileName(para.Item1) + " vs " + Path.GetFileName(para.Item2));

                    var wynik = WyslijZadanie_Failover(indeksSerwera, para.Item1, para.Item2);
                    wyniki.Add(wynik);
                }
            });

            taski.Add(task);
        }

        // czekamy az wszystkie watki skoncza
        Task.WaitAll(taski.ToArray());

        return wyniki.ToList();
    }

    static (string, string, double) WyslijZadanie_Failover(int startIdx, string plikA, string plikB)
    {
        // Próbujemy po kolei każdego serwera z listy
        for (int i = 0; i < adresySerwera.Count; i++)
        {
            int aktualnyIndeks = (startIdx + i) % adresySerwera.Count;
            string adres = adresySerwera[aktualnyIndeks];

            var wynik = WyslijZadanie(adres, plikA, plikB);

            if (wynik.Item3 >= 0)
                return wynik;

            Console.WriteLine($"[FAILOVER] Serwer {adres} zawiódł dla pary {Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)}. Próbuję następny...");
        }

        return (plikA, plikB, -1.0);
    }

    // wysyla dwa pliki do serwera i odbiera wynik podobienstwa
    static (string, string, double) WyslijZadanie(string adres, string plikA, string plikB)
    {
        try
        {
            using (TcpClient klient = new TcpClient())
            {
                klient.ReceiveTimeout = 5000;
                klient.SendTimeout = 5000;
                klient.Connect(adres, port);

                using (NetworkStream stream = klient.GetStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    // mierzymy CALY czas od wyslania do odebrania
                    var stoperCalkowity = System.Diagnostics.Stopwatch.StartNew();

                    writer.Write(n);
                    writer.Write(grainSize);
                    WyslijPlik(writer, plikA);
                    WyslijPlik(writer, plikB);
                    writer.Flush();

                    double jaccard = reader.ReadDouble();
                    double aDoB = reader.ReadDouble();
                    double bDoA = reader.ReadDouble();

                    // odbieramy zakresy pliku 1
                    int liczbaZakresow1 = reader.ReadInt32();
                    var zakresy1 = new List<(int od, int do_)>();
                    for (int i = 0; i < liczbaZakresow1; i++)
                        zakresy1.Add((reader.ReadInt32(), reader.ReadInt32()));

                    // odbieramy zakresy pliku 2
                    int liczbaZakresow2 = reader.ReadInt32();
                    var zakresy2 = new List<(int od, int do_)>();
                    for (int i = 0; i < liczbaZakresow2; i++)
                        zakresy2.Add((reader.ReadInt32(), reader.ReadInt32()));

                    string csv = reader.ReadString();
                    long czasObliczenSerwera = reader.ReadInt64();

                    stoperCalkowity.Stop();
                    long czasCalkowity = stoperCalkowity.ElapsedMilliseconds;
                    long czasTransmisji = czasCalkowity - czasObliczenSerwera;

                    Console.WriteLine($"[CZAS] Obliczenia serwera: {czasObliczenSerwera} ms");
                    Console.WriteLine($"[CZAS] Transmisja:         {czasTransmisji} ms");
                    Console.WriteLine($"[CZAS] Calkowity:          {czasCalkowity} ms");
                    Console.WriteLine($"[ZAKRESY] Plik 1: {liczbaZakresow1} fragmentow");
                    Console.WriteLine($"[ZAKRESY] Plik 2: {liczbaZakresow2} fragmentow");

                    ZapiszCSVLokalnie(plikA, plikB, csv);
                    ZapiszStatystyki(adres, plikA, plikB, jaccard,
                                     czasObliczenSerwera, czasTransmisji, czasCalkowity);

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

            string wynikTekst = podobienstwo < 0 ? "BŁĄD POŁĄCZENIA" : $"{podobienstwo:F2}%";
            string status = (podobienstwo >= prog && podobienstwo >= 0) ? " [PLAGIAT?]" : "";

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

    static void ZapiszStatystyki(string adres, string plikA, string plikB, double jaccard, long czasObliczen, long czasTransmisji, long czasCalkowity)
    {
        string folder = "Raporty";
        Directory.CreateDirectory(folder);

        string plik = Path.Combine(folder, "statystyki.csv");

        bool nowyPlik = !File.Exists(plik);

        using (StreamWriter sw = new StreamWriter(plik, append: true))
        {
            if (nowyPlik)
                sw.WriteLine("Serwer,PlikA,PlikB,Jaccard,CzasObliczen_ms,CzasTransmisji_ms,CzasCalkowity_ms,Timestamp");

            sw.WriteLine(
                adres + "," +
                Path.GetFileName(plikA) + "," +
                Path.GetFileName(plikB) + "," +
                jaccard.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," +
                czasObliczen + "," +
                czasTransmisji + "," +
                czasCalkowity + "," +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            );
        }
    }

    // zapisuje wyniki do pliku CSV
    static void EksportujCSV(List<(string, string, double)> wyniki, string sciezkaWyjsciowa)
    {
        // TODO: StreamWriter, zapisac naglowek i kolejne wiersze
    }

    static void WyswietlPodsumowanie(List<(string, string, double)> wyniki, int liczbaPar, long czasMs)
    {
        // filtrujemy tylko udane wyniki (bez bledow polaczenia)
        var udane = wyniki.Where(w => w.Item3 >= 0).ToList();

        if (udane.Count == 0)
        {
            Console.WriteLine("\n=== PODSUMOWANIE ===");
            Console.WriteLine("Brak wynikow - wszystkie polaczenia zawiodly.");
            return;
        }

        double najwyzsze = udane.Max(w => w.Item3);
        double najnizsze = udane.Min(w => w.Item3);
        double srednie = udane.Average(w => w.Item3);
        int plagiatow = udane.Count(w => w.Item3 >= prog);

        var paraNajwyzszego = udane.First(w => w.Item3 == najwyzsze);
        var paraNajnizszego = udane.First(w => w.Item3 == najnizsze);

        Console.WriteLine("\n=== PODSUMOWANIE ===");
        Console.WriteLine("Przeanalizowano par:   " + liczbaPar);
        Console.WriteLine("Czas calkowity:        " + czasMs + " ms");
        Console.WriteLine("Wykrytych plagiatow:   " + plagiatow);
        Console.WriteLine("Srednie podobienstwo:  " + srednie.ToString("F2") + "%");
        Console.WriteLine("Najwyzsze podobienstwo: " +
            Path.GetFileName(paraNajwyzszego.Item1) + " vs " +
            Path.GetFileName(paraNajwyzszego.Item2) + " = " +
            najwyzsze.ToString("F2") + "%");
        Console.WriteLine("Najnizsze podobienstwo: " +
            Path.GetFileName(paraNajnizszego.Item1) + " vs " +
            Path.GetFileName(paraNajnizszego.Item2) + " = " +
            najnizsze.ToString("F2") + "%");

        // zapisujemy podsumowanie do statystyki
        ZapiszPodsumowanie(liczbaPar, plagiatow, srednie, najwyzsze, najnizsze, czasMs);
    }


    static void ZapiszPodsumowanie(
    int liczbaPar,
    int plagiatow,
    double srednie,
    double najwyzsze,
    double najnizsze,
    long czasMs)
    {
        string folder = "Raporty";
        Directory.CreateDirectory(folder);

        string plik = Path.Combine(folder, "podsumowania.csv");

        bool nowyPlik = !File.Exists(plik);

        using (StreamWriter sw = new StreamWriter(plik, append: true))
        {
            if (nowyPlik)
                sw.WriteLine("LiczbaSerwer ow,LiczbaPar,Plagiatow,Srednie,Najwyzsze,Najnizsze,CzasMs,Timestamp");

            sw.WriteLine(
                adresySerwera.Count + "," +
                liczbaPar + "," +
                plagiatow + "," +
                srednie.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," +
                najwyzsze.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," +
                najnizsze.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "," +
                czasMs + "," +
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            );
        }

        Console.WriteLine("[KLIENT] Zapisano podsumowanie: " + plik);
    }

}

