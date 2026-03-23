using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

class Server
{
    static int port = 8001;

    static void Main()
    {
        TcpListener serwer = new TcpListener(IPAddress.Any, port);
        serwer.Start();
        Console.WriteLine("Serwer dziala na porcie " + port);

        while (true)
        {
            TcpClient klient = serwer.AcceptTcpClient();
            Console.WriteLine("Nowe polaczenie od klienta");
            Task.Run(() => ObsluzKlienta(klient));
        }
    }

    static void ObsluzKlienta(TcpClient klient)
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
        // TODO: podzielic tekst na slowa
        // TODO: przesuwac okno rozmiaru n po liscie slow
        // TODO: kazdy n-gram dodac do HashSet
        return new HashSet<string>();
    }

    // oblicza wspolczynnik Jaccarda dla dwoch zbiorow n-gramow
    static double ObliczJaccard(HashSet<string> ngramyA, HashSet<string> ngramyB)
    {
        // TODO: obliczyc czesc wspolna (intersection)
        // TODO: obliczyc sume zbiorow (union)
        // TODO: podzielic i zwrocic wynik * 100
        return 0.0;
    }

    // sprawdza czy wynik przekracza prog plagiatu
    static bool CzyPlagiat(double podobienstwo, double prog)
    {
        // TODO: prosta comparacja podobienstwo >= prog
        return false;
    }
}