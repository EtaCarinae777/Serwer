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
        private System.Diagnostics.Stopwatch stoper = new System.Diagnostics.Stopwatch();
        private Timer timerInterfejsu;
        private List<WynikPary> wczytaneWyniki = new List<WynikPary>();
        private List<string> _wybranePliki = new List<string>();

        // Lock do bezpiecznego zapisu plików z wielu wątków
        private static readonly object _plikLock = new object();
        private static readonly object _logLock = new object();

        public Form1()
        {
            InitializeComponent();
            listPary.SelectedIndexChanged += ListPary_SelectedIndexChanged;
            listPary.DrawMode = DrawMode.OwnerDrawFixed;
            listPary.DrawItem += ListPary_DrawItem;
            timerInterfejsu = new Timer();
            timerInterfejsu.Interval = 1000;
            timerInterfejsu.Tick += (s, e) => {
                lblTimer.Text = $"Czas: {stoper.Elapsed:hh\\:mm\\:ss}";
            };
        }

        // ── Model danych ─────────────────────────────────────────────────────
        // tekstA/tekstB nie są deserializowane z JSON (serwer ich nie wysyła),
        // GUI wczytuje teksty lokalnie z dysku przy wyborze pary z listy.
        private class WynikPary
        {
            public string plikA { get; set; }
            public string plikB { get; set; }
            public double jaccard { get; set; }
            public double aDoB { get; set; }
            public double bDoA { get; set; }
            public List<Zakres> zakresy1 { get; set; }
            public List<Zakres> zakresy2 { get; set; }
        }

        private class Zakres
        {
            public int od { get; set; }
            public int do_ { get; set; }
        }

        // ── Wybór plików ─────────────────────────────────────────────────────
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

        // ── Rysowanie listy par z kolorowaniem ───────────────────────────────
        private void ListPary_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= wczytaneWyniki.Count) return;

            WynikPary wynik = wczytaneWyniki[e.Index];
            double jaccard = wynik.jaccard;
            double prog = (double)numProg.Value;

            Color tlo = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                ? Color.FromArgb(220, 220, 220)
                : Color.White;

            e.Graphics.FillRectangle(new SolidBrush(tlo), e.Bounds);

            Color kolorTekstu;
            if (jaccard >= prog)
            {
                double t = Math.Min((jaccard - prog) / (100.0 - prog), 1.0);
                int r = 255;
                int g = (int)(140 * (1.0 - t));
                kolorTekstu = Color.FromArgb(r, g, 0);
            }
            else
            {
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

        // ── Główna analiza ───────────────────────────────────────────────────
        private async void btnAnalyzuj_Click(object sender, EventArgs e)
        {
            if (_wybranePliki.Count < 2)
            {
                MessageBox.Show("Wybierz co najmniej 2 pliki!", "Brak plików",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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

            var posortowane = _wybranePliki
                .Where(f => File.Exists(f))
                .OrderByDescending(f => new FileInfo(f).Length)
                .ToList();

            var pary = GenerujPary(posortowane);

            btnAnalyzuj.Enabled = false;
            btnWybierzPliki.Enabled = false;
            progressBar.Minimum = 0;
            progressBar.Maximum = pary.Count;
            progressBar.Value = 0;
            wczytaneWyniki.Clear();
            listPary.Items.Clear();

            stoper.Restart();
            timerInterfejsu.Start();

            int wykonane = 0;
            var wynikiBag = new System.Collections.Concurrent.ConcurrentBag<WynikPary>();

            var progress = new Progress<string>(komunikat =>
            {
                wykonane++;
                progressBar.Value = Math.Min(wykonane, pary.Count);
                lblStatus.Text = komunikat;
            });

            await Task.Run(() =>
            {
                // Ograniczamy do liczby serwerów — każdy serwer obsługuje
                // max 4 żądania równolegle (semaphore po stronie serwera).
                // Klient nie powinien generować więcej wątków niż wynosi
                // łączna przepustowość serwerów.
                int maxWatkow = Math.Min(pary.Count, adresy.Count);

                var opcje = new ParallelOptions { MaxDegreeOfParallelism = maxWatkow };

                Parallel.ForEach(pary, opcje, (para, state, indeks) =>
                {
                    int idx = (int)(indeks % adresy.Count);

                    var wynik = WyslijZadanieFailover(
                        adresy, idx, port, para.Item1, para.Item2, n, grainSize);

                    if (wynik != null)
                        wynikiBag.Add(wynik);

                    ((IProgress<string>)progress).Report(
                        $"{Path.GetFileName(para.Item1)} vs {Path.GetFileName(para.Item2)}");
                });
            });

            stoper.Stop();
            timerInterfejsu.Stop();
            lblTimer.Text = $"Czas całkowity: {stoper.Elapsed:hh\\:mm\\:ss}";

            int oczekiwane = pary.Count;
            int otrzymane = wynikiBag.Count;

            if (oczekiwane != otrzymane)
                MessageBox.Show(
                    $"Oczekiwano par: {oczekiwane}\nOtrzymano wyników: {otrzymane}\nNieudane: {oczekiwane - otrzymane}\n\nSprawdź errors.log w folderze Raporty.",
                    "Uwaga — brakujące wyniki",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);

            wczytaneWyniki = wynikiBag
                .OrderByDescending(w => w.jaccard)
                .ToList();

            OdswiezListe();

            int plagiatow = wczytaneWyniki.Count(w => w.jaccard >= prog);
            lblStatus.Text = $"Gotowe! Przeanalizowano {wczytaneWyniki.Count} par." +
                             (plagiatow > 0 ? $" ⚠ Wykryto {plagiatow} plagiatów!" : "");

            btnAnalyzuj.Enabled = true;
            btnWybierzPliki.Enabled = true;
        }

        // ── Failover — próbuje kolejne serwery gdy jeden zawodzi ─────────────
        private WynikPary WyslijZadanieFailover(
            List<string> adresy, int startIdx, int port,
            string plikA, string plikB, int n, int grainSize)
        {
            for (int i = 0; i < adresy.Count; i++)
            {
                int aktIdx = (startIdx + i) % adresy.Count;
                string adres = adresy[aktIdx];

                var wynik = WyslijZadanie(adres, port, plikA, plikB, n, grainSize);
                if (wynik != null)
                    return wynik;

                ZapiszLog($"[FAILOVER] Serwer {adres}:{port} zawiódł dla pary " +
                          $"{Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)}. Próbuję następny...");
            }

            ZapiszLog($"[BLAD] Wszystkie serwery zawiodły dla pary " +
                      $"{Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)}.");
            return null;
        }

        // ── Wysłanie jednego zadania do serwera ──────────────────────────────
        private WynikPary WyslijZadanie(
            string adres, int port,
            string plikA, string plikB,
            int n, int grainSize)
        {
            try
            {
                using (TcpClient klient = new TcpClient())
                {
                    klient.ReceiveTimeout = 60000;
                    klient.SendTimeout = 60000;
                    klient.Connect(adres, port);

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
                            zakresy1.Add(new Zakres
                            {
                                od = reader.ReadInt32(),
                                do_ = reader.ReadInt32()
                            });

                        int liczbaZakresow2 = reader.ReadInt32();
                        var zakresy2 = new List<Zakres>();
                        for (int i = 0; i < liczbaZakresow2; i++)
                            zakresy2.Add(new Zakres
                            {
                                od = reader.ReadInt32(),
                                do_ = reader.ReadInt32()
                            });

                        string csv = reader.ReadString();
                        string json = reader.ReadString();
                        reader.ReadInt64(); // czasObliczenSerwera — pomijamy

                        ZapiszCSVLokalnie(plikA, plikB, csv);
                        ZapiszJSONLokalnie(plikA, plikB, json);

                        return new WynikPary
                        {
                            plikA = plikA,
                            plikB = plikB,
                            jaccard = jaccard,
                            aDoB = aDoB,
                            bDoA = bDoA,
                            zakresy1 = zakresy1,
                            zakresy2 = zakresy2
                            // tekstA/tekstB celowo puste — wczytywane lazy przy wyborze pary
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

        // ── Wysyłanie pliku strumieniowo — bez ładowania całości do RAM ───────
        // Stara wersja: File.ReadAllBytes() — ładowała cały plik do pamięci.
        // Nowa wersja: czyta i wysyła porcjami po 64KB.
        private void WyslijPlik(BinaryWriter writer, string sciezka)
        {
            string nazwa = Path.GetFileName(sciezka);
            long rozmiar = new FileInfo(sciezka).Length;

            writer.Write(nazwa);
            writer.Write(rozmiar);

            using (FileStream fs = new FileStream(sciezka, FileMode.Open, FileAccess.Read))
            {
                byte[] bufor = new byte[65536]; // 64KB na raz
                int przeczytane;
                while ((przeczytane = fs.Read(bufor, 0, bufor.Length)) > 0)
                    writer.Write(bufor, 0, przeczytane);
            }
        }

        // ── Generowanie par ──────────────────────────────────────────────────
        private List<(string, string)> GenerujPary(List<string> pliki)
        {
            var pary = new List<(string, string)>();
            for (int i = 0; i < pliki.Count; i++)
                for (int j = i + 1; j < pliki.Count; j++)
                    pary.Add((pliki[i], pliki[j]));
            return pary;
        }

        // ── Odświeżenie listy par ────────────────────────────────────────────
        private void OdswiezListe()
        {
            listPary.Items.Clear();
            foreach (var wynik in wczytaneWyniki)
                listPary.Items.Add(
                    Path.GetFileName(wynik.plikA) + " vs " + Path.GetFileName(wynik.plikB));
        }

        // ── Wybór pary z listy — lazy load tekstów z dysku ──────────────────
        // Teksty plików nie są trzymane w pamięci przez cały czas —
        // wczytujemy je dopiero gdy użytkownik kliknie parę.
        private void ListPary_SelectedIndexChanged(object sender, EventArgs e)
        {
            int indeks = listPary.SelectedIndex;
            if (indeks < 0 || indeks >= wczytaneWyniki.Count) return;

            WynikPary wynik = wczytaneWyniki[indeks];

            lblNazwaA.Text = Path.GetFileName(wynik.plikA) +
                             $" (A→B: {wynik.aDoB:F2}%)";
            lblNazwaB.Text = Path.GetFileName(wynik.plikB) +
                             $" (B→A: {wynik.bDoA:F2}%)";

            // Wczytanie tekstu lokalnie — lazy, tylko gdy potrzebne do podglądu
            string tekstA = WczytajTekstLokalnie(wynik.plikA);
            string tekstB = WczytajTekstLokalnie(wynik.plikB);

            rtbPlikA.Text = tekstA;
            rtbPlikB.Text = tekstB;

            PodswietlFragmenty(rtbPlikA, wynik.zakresy1);
            PodswietlFragmenty(rtbPlikB, wynik.zakresy2);
        }

        // Wczytuje tekst z dysku — fallback jeśli plik nie istnieje
        private string WczytajTekstLokalnie(string sciezka)
        {
            try
            {
                if (File.Exists(sciezka))
                    return File.ReadAllText(sciezka);
                return $"[Plik niedostępny: {sciezka}]";
            }
            catch (Exception ex)
            {
                return $"[Błąd odczytu: {ex.Message}]";
            }
        }

        // ── Podświetlanie fragmentów ─────────────────────────────────────────
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
                if (dlugosc <= 0) continue;

                rtb.Select(od, dlugosc);
                rtb.SelectionBackColor = Color.Yellow;
            }

            rtb.Select(0, 0);
        }

        // ── Wczytywanie zapisanych wyników z folderu ─────────────────────────
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
                MessageBox.Show("Nie znaleziono żadnych plików JSON w tym folderze!",
                    "Brak wyników", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int bledy = 0;
            foreach (string plikJson in plikiJson)
            {
                try
                {
                    string zawartosc = File.ReadAllText(plikJson);
                    WynikPary wynik = JsonConvert.DeserializeObject<WynikPary>(zawartosc);

                    // Podstawowa walidacja — pomiń uszkodzone pliki
                    if (wynik == null || wynik.plikA == null || wynik.plikB == null)
                    {
                        bledy++;
                        continue;
                    }

                    wczytaneWyniki.Add(wynik);
                }
                catch
                {
                    bledy++;
                }
            }

            wczytaneWyniki.Sort((a, b) => b.jaccard.CompareTo(a.jaccard));
            OdswiezListe();

            string komunikat = $"Wczytano {wczytaneWyniki.Count} wyników.";
            if (bledy > 0)
                komunikat += $"\nPominięto {bledy} uszkodzonych plików.";

            MessageBox.Show(komunikat, "Wczytywanie zakończone",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ── Zapis CSV i JSON — zabezpieczony przed kolizją nazw ──────────────
        // Stara wersja używała DateTime.Now:yyyyMMdd_HHmmss — dwa wątki
        // uruchomione w tej samej sekundzie nadpisywały sobie pliki.
        // Nowa wersja dodaje losowy suffix (4 znaki hex) eliminujący kolizje.
        private void ZapiszCSVLokalnie(string plikA, string plikB, string csv)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 4);
                string path = Path.Combine("Raporty",
                    $"{Path.GetFileNameWithoutExtension(plikA)}_VS_" +
                    $"{Path.GetFileNameWithoutExtension(plikB)}_" +
                    $"{DateTime.Now:yyyyMMdd_HHmmss}_{suffix}.csv");
                lock (_plikLock)
                    File.WriteAllText(path, csv);
            }
            catch { }
        }

        private void ZapiszJSONLokalnie(string plikA, string plikB, string json)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 4);
                string path = Path.Combine("Raporty",
                    $"{Path.GetFileNameWithoutExtension(plikA)}_VS_" +
                    $"{Path.GetFileNameWithoutExtension(plikB)}_" +
                    $"{DateTime.Now:yyyyMMdd_HHmmss}_{suffix}.json");
                lock (_plikLock)
                    File.WriteAllText(path, json);
            }
            catch { }
        }

        // ── Zapis logów błędów ───────────────────────────────────────────────
        private void ZapiszLog(string komunikat)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string plik = Path.Combine("Raporty", "errors.log");
                string linia = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {komunikat}";
                lock (_logLock)
                    File.AppendAllText(plik, linia + Environment.NewLine);
            }
            catch { }
        }
    }
}