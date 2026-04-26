using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace GUI
{
    public partial class Form1 : Form
    {
        private List<WynikPary> wczytaneWyniki = new List<WynikPary>();
        private List<string> _wybranePliki = new List<string>();

        public Form1()
        {
            InitializeComponent();
            listPary.SelectedIndexChanged += ListPary_SelectedIndexChanged;
            listPary.DrawMode = DrawMode.OwnerDrawFixed;
            listPary.DrawItem += ListPary_DrawItem;
        }

        // ===================================================================
        // Modele danych
        // ===================================================================

        private class WynikPary
        {
            public string plikA { get; set; }
            public string plikB { get; set; }
            public double jaccard { get; set; }
            public double aDoB { get; set; }
            public double bDoA { get; set; }
            public string tekstA { get; set; }
            public string tekstB { get; set; }
            public List<Zakres> zakresy1 { get; set; }
            public List<Zakres> zakresy2 { get; set; }
        }

        private class Zakres
        {
            public int od { get; set; }
            public int do_ { get; set; }
        }

        // ===================================================================
        // NOWE: Wybieranie plików
        // ===================================================================

        private void btnWybierzPliki_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Multiselect = true;
            dialog.Title = "Wybierz pliki do analizy";
            dialog.Filter = "Wszystkie pliki (*.*)|*.*";

            if (dialog.ShowDialog() != DialogResult.OK) return;

            _wybranePliki = new List<string>(dialog.FileNames);
            lblWybranePliki.Text = $"Wybrano {_wybranePliki.Count} plików";
        }
        private void ListPary_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= wczytaneWyniki.Count) return;

            WynikPary wynik = wczytaneWyniki[e.Index];
            double jaccard = wynik.jaccard;
            double prog = (double)numProg.Value;

            // Tło — białe zawsze, ciemniejsze gdy zaznaczony
            Color tlo = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                ? Color.FromArgb(220, 220, 220)
                : Color.White;

            e.Graphics.FillRectangle(new SolidBrush(tlo), e.Bounds);

            // Kolor tekstu — gradient czerwony wg % dla plagiatów, zielony dla czystych
            Color kolorTekstu;
            if (jaccard >= prog)
            {
                // 30% → jasny pomarańczowoczerwony, 100% → intensywna czerwień
                double t = Math.Min((jaccard - prog) / (100.0 - prog), 1.0);
                int r = 255;
                int g = (int)(140 * (1.0 - t)); // 140 → 0
                int b = 0;
                kolorTekstu = Color.FromArgb(r, g, b);
            }
            else
            {
                // czyste — ciemna zieleń
                kolorTekstu = Color.FromArgb(0, 140, 0);
            }

            string tekst = Path.GetFileName(wynik.plikA) + " vs " +
                           Path.GetFileName(wynik.plikB) + "  —  " +
                           jaccard.ToString("F2") + "%" +
                           (jaccard >= prog ? " ⚠" : "");

            e.Graphics.DrawString(tekst, e.Font, new SolidBrush(kolorTekstu),
                e.Bounds.X + 2, e.Bounds.Y + 2);

            e.DrawFocusRectangle();
        }

        // ===================================================================
        // NOWE: Analiza — logika przeniesiona z Klient/Program.cs
        // ===================================================================

        private async void btnAnalyzuj_Click(object sender, EventArgs e)
        {
            if (_wybranePliki.Count < 2)
            {
                MessageBox.Show("Wybierz co najmniej 2 pliki!", "Brak plików",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Odczytaj adresy serwerów z pola tekstowego
            List<string> adresy = txtAdresy.Lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            if (adresy.Count == 0)
            {
                MessageBox.Show("Podaj co najmniej jeden adres serwera!", "Brak serwerów",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int n = (int)numN.Value;
            int grainSize = (int)numGrainSize.Value;
            double prog = (double)numProg.Value;
            int port = 8001;

            // Sortuj pliki malejąco wg rozmiaru (jak w Kliencie)
            var posortowane = _wybranePliki
                .Where(f => File.Exists(f))
                .OrderByDescending(f => new FileInfo(f).Length)
                .ToList();

            var pary = GenerujPary(posortowane);

            // Przygotuj UI
            btnAnalyzuj.Enabled = false;
            btnWybierzPliki.Enabled = false;
            progressBar.Minimum = 0;
            progressBar.Maximum = pary.Count;
            progressBar.Value = 0;
            wczytaneWyniki.Clear();
            listPary.Items.Clear();

            int wykonane = 0;

            // Kolejka par + zadania per serwer (jak RozdzielZadania w Kliencie)
            var kolejka = new System.Collections.Concurrent.ConcurrentQueue<(string, string)>();
            foreach (var para in pary) kolejka.Enqueue(para);

            var wynikiBag = new System.Collections.Concurrent.ConcurrentBag<WynikPary>();

            var progress = new Progress<string>(komunikat =>
            {
                wykonane++;
                progressBar.Value = Math.Min(wykonane, pary.Count);
                lblStatus.Text = komunikat;
            });

            await Task.Run(() =>
            {
                var taski = new List<Task>();

                for (int i = 0; i < adresy.Count; i++)
                {
                    int idx = i;
                    taski.Add(Task.Run(() =>
                    {
                        while (kolejka.TryDequeue(out var para))
                        {
                            var wynik = WyslijZadanieFailover(
                                adresy, idx, port, para.Item1, para.Item2, n, grainSize);

                            if (wynik != null)
                                wynikiBag.Add(wynik);

                            ((IProgress<string>)progress).Report(
                                $"{Path.GetFileName(para.Item1)} vs {Path.GetFileName(para.Item2)}");
                        }
                    }));
                }

                Task.WaitAll(taski.ToArray());
            });

            // Załaduj wyniki bezpośrednio — bez pośrednictwa pliku JSON
            wczytaneWyniki = wynikiBag
                .OrderByDescending(w => w.jaccard)
                .ToList();

            OdswiezListe();

            lblStatus.Text = $"Gotowe! Przeanalizowano {wczytaneWyniki.Count} par.";
            btnAnalyzuj.Enabled = true;
            btnWybierzPliki.Enabled = true;

            int plagiatow = wczytaneWyniki.Count(w => w.jaccard >= prog);
            if (plagiatow > 0)
                lblStatus.Text += $" ⚠ Wykryto {plagiatow} plagiatów!";
        }

        // ===================================================================
        // Logika sieciowa (przeniesiona z Klient/Program.cs)
        // ===================================================================

        private WynikPary WyslijZadanieFailover(
            List<string> adresy, int startIdx, int port,
            string plikA, string plikB, int n, int grainSize)
        {
            for (int i = 0; i < adresy.Count; i++)
            {
                int idx = (startIdx + i) % adresy.Count;
                var wynik = WyslijZadanie(adresy[idx], port, plikA, plikB, n, grainSize);
                if (wynik != null) return wynik;
            }
            return null;
        }

        private WynikPary WyslijZadanie(
     string adres, int port,
     string plikA, string plikB,
     int n, int grainSize)
        {
            try
            {
                using (TcpClient klient = new TcpClient())
                {
                    // timeout na samo Connect — bez tego może wisieć w nieskończoność
                    bool connected = klient.ConnectAsync(adres, port).Wait(3000);
                    if (!connected)
                    {
                        ZapiszLog($"[TIMEOUT] Serwer {adres}:{port} nie odpowiedział w ciągu 3s.");
                        return null;
                    }

                    klient.ReceiveTimeout = 30000;
                    klient.SendTimeout = 30000;

                    using (NetworkStream stream = klient.GetStream())
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        writer.Write(n);
                        writer.Write(grainSize);
                        WyslijPlik(writer, plikA);
                        WyslijPlik(writer, plikB);
                        writer.Flush();

                        double jaccard = reader.ReadDouble();
                        double aDoB = reader.ReadDouble();
                        double bDoA = reader.ReadDouble();

                        int liczbaZakresow1 = reader.ReadInt32();
                        var zakresy1 = new List<Zakres>();
                        for (int i = 0; i < liczbaZakresow1; i++)
                            zakresy1.Add(new Zakres { od = reader.ReadInt32(), do_ = reader.ReadInt32() });

                        int liczbaZakresow2 = reader.ReadInt32();
                        var zakresy2 = new List<Zakres>();
                        for (int i = 0; i < liczbaZakresow2; i++)
                            zakresy2.Add(new Zakres { od = reader.ReadInt32(), do_ = reader.ReadInt32() });

                        string csv = reader.ReadString();
                        string json = reader.ReadString();
                        reader.ReadInt64();

                        ZapiszCSVLokalnie(plikA, plikB, csv);
                        ZapiszJSONLokalnie(plikA, plikB, json);

                        WynikPary wynikZJson = null;
                        try { wynikZJson = JsonConvert.DeserializeObject<WynikPary>(json); } catch { }

                        return new WynikPary
                        {
                            plikA = plikA,
                            plikB = plikB,
                            jaccard = jaccard,
                            aDoB = aDoB,
                            bDoA = bDoA,
                            tekstA = wynikZJson?.tekstA ?? File.ReadAllText(plikA),
                            tekstB = wynikZJson?.tekstB ?? File.ReadAllText(plikB),
                            zakresy1 = zakresy1,
                            zakresy2 = zakresy2
                        };
                    }
                }
            }
            catch (SocketException ex)
            {
                ZapiszLog($"[SOCKET ERROR] Serwer {adres}:{port} — {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                ZapiszLog($"[IO ERROR] Serwer {adres}:{port} zerwał połączenie — {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                ZapiszLog($"[ERROR] Serwer {adres}:{port} — {ex.Message}");
                return null;
            }
        }

        // Dopisz tę metodę obok pozostałych pomocniczych:
        private static readonly object _logLock = new object();

        private void ZapiszLog(string komunikat)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string plik = Path.Combine("Raporty", "errors.log");
                string linia = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {komunikat}";

                lock (_logLock)  // kilka wątków może pisać jednocześnie
                {
                    File.AppendAllText(plik, linia + Environment.NewLine);
                }
            }
            catch { }
        }

        private void WyslijPlik(BinaryWriter writer, string sciezka)
        {
            byte[] dane = File.ReadAllBytes(sciezka);
            writer.Write(Path.GetFileName(sciezka));
            writer.Write((long)dane.Length);
            writer.Write(dane);
        }

        // ===================================================================
        // Pomocnicze — generowanie par
        // ===================================================================

        private List<(string, string)> GenerujPary(List<string> pliki)
        {
            var pary = new List<(string, string)>();
            for (int i = 0; i < pliki.Count; i++)
                for (int j = i + 1; j < pliki.Count; j++)
                    pary.Add((pliki[i], pliki[j]));
            return pary;
        }

        // ===================================================================
        // Zapis raportów na dysk (identycznie jak w Kliencie)
        // ===================================================================

        private void ZapiszCSVLokalnie(string plikA, string plikB, string csv)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string path = Path.Combine("Raporty",
                    $"{Path.GetFileNameWithoutExtension(plikA)}_VS_" +
                    $"{Path.GetFileNameWithoutExtension(plikB)}_" +
                    $"{DateTime.Now:yyyyMMdd_HHmmss}.csv");
                File.WriteAllText(path, csv);
            }
            catch { /* nie przerywaj analizy jeśli zapis się nie uda */ }
        }

        private void ZapiszJSONLokalnie(string plikA, string plikB, string json)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string path = Path.Combine("Raporty",
                    $"{Path.GetFileNameWithoutExtension(plikA)}_VS_" +
                    $"{Path.GetFileNameWithoutExtension(plikB)}_" +
                    $"{DateTime.Now:yyyyMMdd_HHmmss}.json");
                File.WriteAllText(path, json);
            }
            catch { }
        }

        // ===================================================================
        // Wczytywanie wyników z folderu (istniejąca funkcja)
        // ===================================================================

        private void btnWczytaj_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "Wybierz folder z wynikami (Raporty)";

            if (dialog.ShowDialog() != DialogResult.OK) return;

            string folder = dialog.SelectedPath;
            lblFolder.Text = folder;

            wczytaneWyniki.Clear();
            listPary.Items.Clear();

            string[] plikiJson = Directory.GetFiles(folder, "*.json");

            if (plikiJson.Length == 0)
            {
                MessageBox.Show("Nie znaleziono żadnych plików JSON w tym folderze!");
                return;
            }

            foreach (string plikJson in plikiJson)
            {
                try
                {
                    string zawartosc = File.ReadAllText(plikJson);
                    WynikPary wynik = JsonConvert.DeserializeObject<WynikPary>(zawartosc);
                    wczytaneWyniki.Add(wynik);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Blad wczytywania: " + ex.Message);
                }
            }

            wczytaneWyniki.Sort((a, b) => b.jaccard.CompareTo(a.jaccard));
            OdswiezListe();
            MessageBox.Show("Wczytano " + wczytaneWyniki.Count + " wyników!");
        }

        // ===================================================================
        // UI — lista i podświetlanie
        // ===================================================================

        private void OdswiezListe()
        {
            listPary.Items.Clear();
            foreach (var wynik in wczytaneWyniki)
                listPary.Items.Add(wynik.plikA); // tekst nieważny, DrawItem go nadpisuje
        }

        private void ListPary_SelectedIndexChanged(object sender, EventArgs e)
        {
            int indeks = listPary.SelectedIndex;
            if (indeks < 0 || indeks >= wczytaneWyniki.Count) return;

            WynikPary wynik = wczytaneWyniki[indeks];

            lblNazwaA.Text = Path.GetFileName(wynik.plikA) +
                             " (A→B: " + wynik.aDoB.ToString("F2") + "%)";
            lblNazwaB.Text = Path.GetFileName(wynik.plikB) +
                             " (B→A: " + wynik.bDoA.ToString("F2") + "%)";

            rtbPlikA.Text = wynik.tekstA;
            rtbPlikB.Text = wynik.tekstB;

            PodswietlFragmenty(rtbPlikA, wynik.zakresy1);
            PodswietlFragmenty(rtbPlikB, wynik.zakresy2);
        }

        private void PodswietlFragmenty(RichTextBox rtb, List<Zakres> zakresy)
        {
            rtb.SelectAll();
            rtb.SelectionBackColor = Color.White;

            if (zakresy == null) return;

            foreach (var zakres in zakresy)
            {
                int od = zakres.od;
                int dlugosc = zakres.do_ - zakres.od;

                if (od < 0 || od >= rtb.Text.Length) continue;
                if (od + dlugosc > rtb.Text.Length)
                    dlugosc = rtb.Text.Length - od;

                rtb.Select(od, dlugosc);
                rtb.SelectionBackColor = Color.Yellow;
            }

            rtb.Select(0, 0);
        }
    }
}