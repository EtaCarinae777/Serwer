using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Collections.Generic;
using System.Linq;

class Client
{
    static List<string> adresySerwera = new List<string>();
    static int port = 8001;
    static int n = 4;
    static int grainSize = 1;
    static double prog = 30.0;
    static bool trybDebug = true; // zmien na true zeby widziec logi

    static void Main(string[] args)
    {
        WczytajKonfiguracje();

        List<string> posortowane = WczytajIposortuj(PobierzSciezkiOdUzytkownika());
        List<(string, string)> pary = GenerujPary(posortowane);
        WyswietlPary(pary);

        var stoperCalkowity = System.Diagnostics.Stopwatch.StartNew();
        var wyniki = RozdzielZadania(pary, posortowane);
        stoperCalkowity.Stop();
        long czasCalkowitejAnalizy = stoperCalkowity.ElapsedMilliseconds;

        Console.WriteLine($"\nCzas calkowity analizy: {czasCalkowitejAnalizy} ms");
    }

    static void WczytajKonfiguracje()
    {
        string plikKonfiguracji = "config.txt";

        if (!File.Exists(plikKonfiguracji))
        {
            File.WriteAllText(plikKonfiguracji,
                "# Adresy serwerow (jeden na linie)\n" +
                "127.0.0.1\n\n" +
                "# Parametry\n" +
                "n=4\n" +
                "grainSize=1\n" +
                "prog=30\n" +
                "port=8001\n");
            Console.WriteLine("[CONFIG] Utworzono domyslny plik config.txt");
        }

        foreach (string linia in File.ReadAllLines(plikKonfiguracji))
        {
            string trimmed = linia.Trim();
            if (trimmed.StartsWith("#") || trimmed.Length == 0) continue;

            if (trimmed.Contains("="))
            {
                string[] czesci = trimmed.Split('=');
                string klucz = czesci[0].Trim();
                string wartosc = czesci[1].Trim();

                switch (klucz)
                {
                    case "n": n = int.Parse(wartosc); break;
                    case "grainSize": grainSize = int.Parse(wartosc); break;
                    case "prog": prog = double.Parse(wartosc, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "port": port = int.Parse(wartosc); break;
                }
            }
            else
            {
                adresySerwera.Add(trimmed);
            }
        }

        if (adresySerwera.Count == 0)
            adresySerwera.Add("127.0.0.1");

        Console.WriteLine("[CONFIG] Wczytano konfiguracje:");
        Console.WriteLine("[CONFIG] Serwery: " + string.Join(", ", adresySerwera));
        Console.WriteLine("[CONFIG] n=" + n + " grainSize=" + grainSize + " prog=" + prog + " port=" + port);
    }

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

        pliki.Sort((a, b) => new FileInfo(b).Length.CompareTo(new FileInfo(a).Length));

        Console.WriteLine("\n=== PLIKI PO POSORTOWANIU ===");
        foreach (string plik in pliki)
        {
            long rozmiar = new FileInfo(plik).Length;
            Console.WriteLine(Path.GetFileName(plik) + " - " + rozmiar + " bajtow");
        }

        return pliki;
    }

    static List<(string, string)> GenerujPary(List<string> pliki)
    {
        List<(string, string)> pary = new List<(string, string)>();

        for (int i = 0; i < pliki.Count; i++)
            for (int j = i + 1; j < pliki.Count; j++)
                pary.Add((pliki[i], pliki[j]));

        return pary;
    }

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

    static void WyslijPlik(BinaryWriter writer, string sciezka)
    {
        byte[] dane = File.ReadAllBytes(sciezka);
        string nazwa = Path.GetFileName(sciezka);
        writer.Write(nazwa);
        writer.Write((long)dane.Length);
        writer.Write(dane);
    }

    static List<(string, string, double)> RozdzielZadania(List<(string, string)> pary, List<string> pliki)
    {
        var kolejka = new System.Collections.Concurrent.ConcurrentQueue<(string, string)>();
        foreach (var para in pary)
            kolejka.Enqueue(para);

        var wyniki = new System.Collections.Concurrent.ConcurrentBag<(string, string, double)>();

        int maxWatkow = Math.Min(
            pary.Count,
            Math.Min(
                adresySerwera.Count,
                Environment.ProcessorCount * 2
            )
        );

        if (trybDebug)
            Console.WriteLine($"[KLIENT] Uruchamiam {maxWatkow} watkow dla {pary.Count} par i {adresySerwera.Count} serwerow");

        var opcje = new ParallelOptions { MaxDegreeOfParallelism = maxWatkow };

        Parallel.ForEach(pary, opcje, (para, state, indeks) =>
        {
            int indeksSerwera = (int)(indeks % adresySerwera.Count);

            if (trybDebug)
                Console.WriteLine($"[KLIENT] Serwer {adresySerwera[indeksSerwera]} pobral pare: " +
                    Path.GetFileName(para.Item1) + " vs " + Path.GetFileName(para.Item2));

            var wynik = WyslijZadanie_Failover(indeksSerwera, para.Item1, para.Item2);
            wyniki.Add(wynik);
        });

        return wyniki.ToList();
    }

    static (string, string, double) WyslijZadanie_Failover(int startIdx, string plikA, string plikB)
    {
        for (int i = 0; i < adresySerwera.Count; i++)
        {
            int aktualnyIndeks = (startIdx + i) % adresySerwera.Count;
            string adres = adresySerwera[aktualnyIndeks];

            var wynik = WyslijZadanie(adres, plikA, plikB);

            if (wynik.Item3 >= 0)
                return wynik;

            if (trybDebug)
                Console.WriteLine($"[FAILOVER] Serwer {adres} zawiodl dla pary {Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)}. Probuje nastepny...");
        }

        return (plikA, plikB, -1.0);
    }

    static (string, string, double) WyslijZadanie(string adres, string plikA, string plikB)
    {
        try
        {
            using (TcpClient klient = new TcpClient())
            {
                klient.ReceiveTimeout = 30000;//TODO testowanie na 30 sekundach
                klient.SendTimeout = 30000;
                klient.Connect(adres, port);

                using (NetworkStream stream = klient.GetStream())
                using (BinaryWriter writer = new BinaryWriter(stream))
                using (BinaryReader reader = new BinaryReader(stream))
                {
                    var stoperCalkowity = System.Diagnostics.Stopwatch.StartNew();

                    writer.Write(n);
                    writer.Write(grainSize);
                    WyslijPlik(writer, plikA);
                    WyslijPlik(writer, plikB);
                    writer.Flush();

                    double jaccard = reader.ReadDouble();
                    double aDoB = reader.ReadDouble();
                    double bDoA = reader.ReadDouble();

                    int liczbaZakresow1 = reader.ReadInt32();
                    var zakresy1 = new List<(int od, int do_)>();
                    for (int i = 0; i < liczbaZakresow1; i++)
                        zakresy1.Add((reader.ReadInt32(), reader.ReadInt32()));

                    int liczbaZakresow2 = reader.ReadInt32();
                    var zakresy2 = new List<(int od, int do_)>();
                    for (int i = 0; i < liczbaZakresow2; i++)
                        zakresy2.Add((reader.ReadInt32(), reader.ReadInt32()));

                    string csv = reader.ReadString();
                    string json = reader.ReadString();
                    long czasObliczenSerwera = reader.ReadInt64();

                    stoperCalkowity.Stop();
                    long czasCalkowity = stoperCalkowity.ElapsedMilliseconds;
                    long czasTransmisji = czasCalkowity - czasObliczenSerwera;

                    if (trybDebug)
                    {
                        Console.WriteLine($"[CZAS] Obliczenia serwera: {czasObliczenSerwera} ms");
                        Console.WriteLine($"[CZAS] Transmisja:         {czasTransmisji} ms");
                        Console.WriteLine($"[CZAS] Calkowity:          {czasCalkowity} ms");
                        Console.WriteLine($"[ZAKRESY] Plik 1: {liczbaZakresow1} fragmentow");
                        Console.WriteLine($"[ZAKRESY] Plik 2: {liczbaZakresow2} fragmentow");
                    }

                    ZapiszCSVLokalnie(plikA, plikB, csv);
                    ZapiszJSONLokalnie(plikA, plikB, json);
                    ZapiszStatystyki(adres, plikA, plikB, jaccard,
                                     czasObliczenSerwera, czasTransmisji, czasCalkowity);

                    return (plikA, plikB, jaccard);
                }
            }
        }
        catch (SocketException)
        {
            if (trybDebug)
                Console.WriteLine($"[BLAD SIECI] {adres}: Serwer jest wylaczony lub port zablokowany.");
            return (plikA, plikB, -1);
        }
        catch (IOException)
        {
            if (trybDebug)
                Console.WriteLine($"[BLAD TRANSMISJI] {adres}: Serwer zerwal polaczenie.");
            return (plikA, plikB, -1);
        }
        catch (Exception ex)
        {
            if (trybDebug)
                Console.WriteLine($"[BLAD NIEOCZEKIWANY] {adres}: {ex.Message}");
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

        if (trybDebug)
            Console.WriteLine($"[KLIENT] Zapisano raport: {path}");
    }

    static void ZapiszJSONLokalnie(string plikA, string plikB, string json)
    {
        string folder = "Raporty";
        Directory.CreateDirectory(folder);

        string nameA = Path.GetFileNameWithoutExtension(plikA);
        string nameB = Path.GetFileNameWithoutExtension(plikB);

        string path = Path.Combine(folder,
            $"{nameA}_VS_{nameB}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

        File.WriteAllText(path, json);

        if (trybDebug)
            Console.WriteLine($"[KLIENT] Zapisano JSON: {path}");
    }

    static void ZapiszStatystyki(string adres, string plikA, string plikB, double jaccard,
        long czasObliczen, long czasTransmisji, long czasCalkowity)
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

    static void WyswietlMacierz(List<(string, string, double)> wyniki, List<string> pliki)
    {
        Console.WriteLine("\n=== WYNIKI ===");
        foreach (var (a, b, podobienstwo) in wyniki)
        {
            string nazwaA = Path.GetFileName(a);
            string nazwaB = Path.GetFileName(b);
            string wynikTekst = podobienstwo < 0 ? "BLAD POLACZENIA" : $"{podobienstwo:F2}%";
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

    static void WyswietlPodsumowanie(List<(string, string, double)> wyniki, int liczbaPar, long czasMs)
    {
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

        ZapiszPodsumowanie(liczbaPar, plagiatow, srednie, najwyzsze, najnizsze, czasMs);
    }

    static void ZapiszPodsumowanie(int liczbaPar, int plagiatow, double srednie,
        double najwyzsze, double najnizsze, long czasMs)
    {
        string folder = "Raporty";
        Directory.CreateDirectory(folder);

        string plik = Path.Combine(folder, "podsumowania.csv");
        bool nowyPlik = !File.Exists(plik);

        using (StreamWriter sw = new StreamWriter(plik, append: true))
        {
            if (nowyPlik)
                sw.WriteLine("LiczbaSerwerow,LiczbaPar,Plagiatow,Srednie,Najwyzsze,Najnizsze,CzasMs,Timestamp");

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

        if (trybDebug)
            Console.WriteLine("[KLIENT] Zapisano podsumowanie.");
    }

    static void EksportujCSV(List<(string, string, double)> wyniki, string sciezkaWyjsciowa)
    {
        // TODO: StreamWriter, zapisac naglowek i kolejne wiersze
    }
}