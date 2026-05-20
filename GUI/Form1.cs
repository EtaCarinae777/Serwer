using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json;
using Timer = System.Windows.Forms.Timer;

namespace GUI
{
    public partial class Form1 : Form
    {
        private System.Diagnostics.Stopwatch stoper = new System.Diagnostics.Stopwatch();
        private Timer timerInterfejsu;
        private List<WynikPary> wczytaneWyniki = new List<WynikPary>();
        private List<string> _wybranePliki = new List<string>();

        private static readonly object _plikLock = new object();
        private static readonly object _logLock = new object();

        private const int SLOTOW_NA_SERWER = 8;
        private int _licznikZadan = -1;
        const int TIMEOUT_MS = 300_000;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam);
        const int WM_SETREDRAW = 11;

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

        // ── Stan lazy uploadu ─────────────────────────────────────────────────
        // fileIds[adres][sciezka] = file_id — wypełniany leniwie przy pierwszym COMPARE
        private Dictionary<string, Dictionary<string, string>> _fileIds;
        // Locki per (adres, sciezka) — żeby ten sam plik nie był uploadowany 2× na ten sam serwer
        private Dictionary<string, SemaphoreSlim> _uploadLocks;
        private static readonly object _uploadLocksLock = new object();

        private SemaphoreSlim PobierzUploadLock(string adres, string sciezka)
        {
            string klucz = $"{adres}|{sciezka}";
            lock (_uploadLocksLock)
            {
                if (!_uploadLocks.TryGetValue(klucz, out var sem))
                {
                    sem = new SemaphoreSlim(1, 1);
                    _uploadLocks[klucz] = sem;
                }
                return sem;
            }
        }

        // Zapewnia że plik jest na serwerze — uploaduje jeśli jeszcze nie był.
        // Zwraca file_id lub null przy błędzie.
        private string ZapewnijUpload(string adres, int port, string sciezka)
        {
            // Szybka ścieżka — już jest
            lock (_fileIds)
            {
                if (_fileIds[adres].TryGetValue(sciezka, out string id))
                    return id;
            }

            // Wolna ścieżka — upload z lockiem per (adres, sciezka)
            var sem = PobierzUploadLock(adres, sciezka);
            sem.Wait();
            try
            {
                // Sprawdź ponownie po wejściu do locka
                lock (_fileIds)
                {
                    if (_fileIds[adres].TryGetValue(sciezka, out string id))
                        return id;
                }

                string fileId = WyslijUpload(adres, port, sciezka);
                if (fileId == null)
                {
                    ZapiszLog($"[UPLOAD BLAD] {adres} - {Path.GetFileName(sciezka)}");
                    return null;
                }

                lock (_fileIds)
                    _fileIds[adres][sciezka] = fileId;

                return fileId;
            }
            finally
            {
                sem.Release();
            }
        }

        // ── Wybor plikow ─────────────────────────────────────────────────────
        private void btnWybierzPliki_Click(object sender, EventArgs e)
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Multiselect = true;
            dialog.Title = "Wybierz pliki do analizy";
            dialog.Filter = "Wszystkie pliki (*.*)|*.*";

            if (dialog.ShowDialog() != DialogResult.OK) return;

            _wybranePliki = new List<string>(dialog.FileNames);
            lblWybranePliki.Text = $"Wybrano {_wybranePliki.Count} plikow";
        }

        // ── Kolorowanie listy ─────────────────────────────────────────────────
        private void ListPary_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= wczytaneWyniki.Count) return;

            WynikPary wynik = wczytaneWyniki[e.Index];
            double jaccard = wynik.jaccard;
            double prog = (double)numProg.Value;

            Color tlo = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                ? Color.FromArgb(220, 220, 220)
                : Color.White;

            using (SolidBrush tloB = new SolidBrush(tlo))
                e.Graphics.FillRectangle(tloB, e.Bounds);

            Color kolorTekstu;
            if (jaccard >= prog)
            {
                double t = Math.Min((jaccard - prog) / (100.0 - prog), 1.0);
                kolorTekstu = Color.FromArgb(255, (int)(140 * (1.0 - t)), 0);
            }
            else
            {
                kolorTekstu = Color.FromArgb(0, 140, 0);
            }

            string tekst = Path.GetFileName(wynik.plikA) + " vs " +
                           Path.GetFileName(wynik.plikB) + "  -  " +
                           jaccard.ToString("F2") + "%" +
                           (jaccard >= prog ? " !" : "");

            using (SolidBrush tekstB = new SolidBrush(kolorTekstu))
                e.Graphics.DrawString(tekst, e.Font, tekstB, e.Bounds.X + 2, e.Bounds.Y + 2);

            e.DrawFocusRectangle();
        }

        // ── Glowna analiza ───────────────────────────────────────────────────
        private async void btnAnalyzuj_Click(object sender, EventArgs e)
        {
            if (_wybranePliki.Count < 2)
            {
                MessageBox.Show("Wybierz co najmniej 2 pliki!", "Brak plikow",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> adresy = txtAdresy.Lines
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToList();

            if (adresy.Count == 0)
            {
                MessageBox.Show("Podaj co najmniej jeden adres serwera!", "Brak serwerow",
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
            wczytaneWyniki.Clear();
            listPary.Items.Clear();

            stoper.Restart();
            timerInterfejsu.Start();
            _licznikZadan = -1;

            // Inicjalizacja stanu lazy uploadu
            _fileIds = new Dictionary<string, Dictionary<string, string>>();
            _uploadLocks = new Dictionary<string, SemaphoreSlim>();
            foreach (var adres in adresy)
                _fileIds[adres] = new Dictionary<string, string>();

            // ── FAZA 1 (lazy): brak wstępnego uploadu — pliki trafią na serwery
            //    dopiero gdy dana para zostanie przydzielona do konkretnego serwera.
            // ── FAZA 2: porownywanie par ─────────────────────────────────────
            lblStatus.Text = "Porownywanie par (upload lazily)...";
            progressBar.Minimum = 0;
            progressBar.Maximum = pary.Count;
            progressBar.Value = 0;

            int wykonane = 0;
            var wynikiBag = new System.Collections.Concurrent.ConcurrentBag<WynikPary>();
            int liczbaSerwerow = adresy.Count;

            var swTotal = System.Diagnostics.Stopwatch.StartNew();

            await Task.Run(() =>
            {
                int maxWatkow = Math.Min(liczbaSerwerow * SLOTOW_NA_SERWER, pary.Count);
                var opcje = new ParallelOptions { MaxDegreeOfParallelism = maxWatkow };

                Parallel.ForEach(pary, opcje, (para) =>
                {
                    int idx = (int)((uint)Interlocked.Increment(ref _licznikZadan) % liczbaSerwerow);

                    var wynik = WyslijCompareFailover(
                        adresy, idx, port,
                        para.Item1, para.Item2, n, grainSize);

                    if (wynik != null)
                        wynikiBag.Add(wynik);

                    int done = Interlocked.Increment(ref wykonane);
                    BeginInvoke(new Action(() =>
                    {
                        progressBar.Value = Math.Min(done, pary.Count);
                        lblStatus.Text = $"Para {done}/{pary.Count}: {Path.GetFileName(para.Item1)} vs {Path.GetFileName(para.Item2)}";
                    }));
                });
            });

            swTotal.Stop();
            ZapiszLog($"[TOTAL] {pary.Count} par, {liczbaSerwerow} serwerow: {swTotal.ElapsedMilliseconds}ms");

            // Statystyki uploadu
            foreach (var adres in adresy)
            {
                int uploadCount;
                lock (_fileIds)
                    uploadCount = _fileIds[adres].Count;
                ZapiszLog($"[UPLOAD LAZY] {adres}: przeslano {uploadCount}/{posortowane.Count} plikow");
            }

            stoper.Stop();
            timerInterfejsu.Stop();
            lblTimer.Text = $"Czas calkowity: {stoper.Elapsed:hh\\:mm\\:ss}";

            int oczekiwane = pary.Count;
            int otrzymane = wynikiBag.Count;

            if (oczekiwane != otrzymane)
                MessageBox.Show(
                    $"Oczekiwano par: {oczekiwane}\nOtrzymano wynikow: {otrzymane}\n" +
                    $"Nieudane: {oczekiwane - otrzymane}\n\nSprawdz errors.log w folderze Raporty.",
                    "Uwaga - brakujace wyniki",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);

            wczytaneWyniki = wynikiBag
                .OrderByDescending(w => w.jaccard)
                .ToList();

            OdswiezListe();

            int plagiatow = wczytaneWyniki.Count(w => w.jaccard >= prog);
            lblStatus.Text = $"Gotowe! Przeanalizowano {wczytaneWyniki.Count} par." +
                             (plagiatow > 0 ? $" Wykryto {plagiatow} plagiatow!" : "");

            btnAnalyzuj.Enabled = true;
            btnWybierzPliki.Enabled = true;
        }

        // ── Upload pliku na serwer ────────────────────────────────────────────
        // Protokol: [0x01] [nazwa:string] [rozmiar:int64] [dane:bytes]
        // Odpowiedz: [file_id:string] [juz_w_cache:bool]
        private string WyslijUpload(string adres, int port, string sciezka)
        {
            try
            {
                byte[] dane = File.ReadAllBytes(sciezka);
                string nazwa = Path.GetFileName(sciezka);

                using (TcpClient klient = new TcpClient())
                {
                    klient.ReceiveTimeout = TIMEOUT_MS;
                    klient.SendTimeout = TIMEOUT_MS;
                    klient.Connect(adres, port);

                    using (NetworkStream stream = klient.GetStream())
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        writer.Write((byte)0x01);
                        writer.Write(nazwa);
                        writer.Write((long)dane.Length);

                        int wyslane = 0;
                        while (wyslane < dane.Length)
                        {
                            int porcja = Math.Min(65536, dane.Length - wyslane);
                            writer.Write(dane, wyslane, porcja);
                            wyslane += porcja;
                        }
                        writer.Flush();

                        string fileId = reader.ReadString();
                        bool wCache = reader.ReadBoolean();
                        ZapiszLog($"[UPLOAD] {Path.GetFileName(sciezka)} → {adres} ({(wCache ? "cache hit" : "nowy")})");
                        return fileId;
                    }
                }
            }
            catch (Exception ex)
            {
                ZapiszLog($"[UPLOAD ERROR] {adres}:{port} - {Path.GetFileName(sciezka)} - {ex.Message}");
                return null;
            }
        }

        // ── Compare z failoverem i lazy uploadem ──────────────────────────────
        private WynikPary WyslijCompareFailover(
            List<string> adresy,
            int startIdx, int port,
            string plikA, string plikB, int n, int grainSize)
        {
            for (int i = 0; i < adresy.Count; i++)
            {
                int aktIdx = (startIdx + i) % adresy.Count;
                string adres = adresy[aktIdx];

                // Lazy upload — uploaduj plik tylko jeśli ten serwer jeszcze go nie ma
                string idA = ZapewnijUpload(adres, port, plikA);
                if (idA == null)
                {
                    ZapiszLog($"[FAILOVER] Upload A zawiodl na {adres}, probuje nastepny serwer...");
                    continue;
                }

                string idB = ZapewnijUpload(adres, port, plikB);
                if (idB == null)
                {
                    ZapiszLog($"[FAILOVER] Upload B zawiodl na {adres}, probuje nastepny serwer...");
                    continue;
                }

                var wynik = WyslijCompare(adres, port, idA, idB, plikA, plikB, n, grainSize);
                if (wynik != null)
                    return wynik;

                ZapiszLog($"[FAILOVER] COMPARE na {adres}:{port} zawiodlo dla pary " +
                          $"{Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)}. Probuje nastepny...");
            }

            ZapiszLog($"[BLAD] Wszystkie serwery zawiodly dla pary " +
                      $"{Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)}.");
            return null;
        }

        // ── Wyslanie COMPARE ──────────────────────────────────────────────────
        // Protokol: [0x02] [fileId_A:string] [fileId_B:string] [n:int32] [grainSize:int32]
        private WynikPary WyslijCompare(
            string adres, int port,
            string fileIdA, string fileIdB,
            string plikA, string plikB,
            int n, int grainSize)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using (TcpClient klient = new TcpClient())
                {
                    klient.ReceiveTimeout = TIMEOUT_MS;
                    klient.SendTimeout = TIMEOUT_MS;
                    klient.Connect(adres, port);

                    using (NetworkStream stream = klient.GetStream())
                    using (BinaryWriter writer = new BinaryWriter(stream))
                    using (BinaryReader reader = new BinaryReader(stream))
                    {
                        writer.Write((byte)0x02);
                        writer.Write(fileIdA);
                        writer.Write(fileIdB);
                        writer.Write(n);
                        writer.Write(grainSize);
                        writer.Flush();

                        double jaccard = reader.ReadDouble();

                        if (jaccard < 0)
                        {
                            ZapiszLog($"[COMPARE ERROR] Serwer {adres} nie rozpoznal file_id.");
                            return null;
                        }

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

                        sw.Stop();
                        ZapiszLog($"[CZAS] {Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)} @ {adres} - connect+compare: {sw.ElapsedMilliseconds}ms");

                        return new WynikPary
                        {
                            plikA = plikA,
                            plikB = plikB,
                            jaccard = jaccard,
                            aDoB = aDoB,
                            bDoA = bDoA,
                            zakresy1 = zakresy1,
                            zakresy2 = zakresy2
                        };
                    }
                }
            }
            catch (SocketException ex)
            {
                ZapiszLog($"[SOCKET ERROR] {adres}:{port} - {ex.Message}");
                return null;
            }
            catch (IOException ex)
            {
                ZapiszLog($"[IO ERROR] {adres}:{port} - {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                ZapiszLog($"[ERROR] {adres}:{port} - {ex.Message}");
                return null;
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

        // ── Lista par ────────────────────────────────────────────────────────
        private void OdswiezListe()
        {
            listPary.BeginUpdate();
            listPary.Items.Clear();
            foreach (var wynik in wczytaneWyniki)
                listPary.Items.Add(
                    Path.GetFileName(wynik.plikA) + " vs " + Path.GetFileName(wynik.plikB));
            listPary.EndUpdate();
        }

        // ── Wybor pary ───────────────────────────────────────────────────────
        private void ListPary_SelectedIndexChanged(object sender, EventArgs e)
        {
            int indeks = listPary.SelectedIndex;
            if (indeks < 0 || indeks >= wczytaneWyniki.Count) return;

            WynikPary wynik = wczytaneWyniki[indeks];

            lblNazwaA.Text = Path.GetFileName(wynik.plikA) + $" (A-B: {wynik.aDoB:F2}%)";
            lblNazwaB.Text = Path.GetFileName(wynik.plikB) + $" (B-A: {wynik.bDoA:F2}%)";

            string tekstA = WczytajTekstLokalnie(wynik.plikA);
            string tekstB = WczytajTekstLokalnie(wynik.plikB);

            rtbPlikA.Text = tekstA;
            rtbPlikB.Text = tekstB;

            PodswietlFragmenty(rtbPlikA, wynik.zakresy1);
            PodswietlFragmenty(rtbPlikB, wynik.zakresy2);
        }

        private string WczytajTekstLokalnie(string sciezka)
        {
            try
            {
                if (File.Exists(sciezka))
                    return File.ReadAllText(sciezka);
                return $"[Plik niedostepny: {sciezka}]";
            }
            catch (Exception ex)
            {
                return $"[Blad odczytu: {ex.Message}]";
            }
        }

        // ── Podswietlanie ────────────────────────────────────────────────────
        private void PodswietlFragmenty(RichTextBox rtb, List<Zakres> zakresy)
        {
            SendMessage(rtb.Handle, WM_SETREDRAW, false, 0);

            try
            {
                rtb.SelectAll();
                rtb.SelectionBackColor = Color.White;

                if (zakresy != null)
                {
                    foreach (var zakres in zakresy)
                    {
                        int od = zakres.od;
                        int dlugosc = zakres.do_ - zakres.od;

                        if (od < 0 || od >= rtb.TextLength) continue;
                        if (od + dlugosc > rtb.TextLength)
                            dlugosc = rtb.TextLength - od;
                        if (dlugosc <= 0) continue;

                        rtb.Select(od, dlugosc);
                        rtb.SelectionBackColor = Color.Yellow;
                    }
                }

                rtb.Select(0, 0);
            }
            finally
            {
                SendMessage(rtb.Handle, WM_SETREDRAW, true, 0);
                rtb.Invalidate();
            }
        }

        // ── Wczytywanie wynikow z folderu ────────────────────────────────────
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
                MessageBox.Show("Nie znaleziono zadnych plikow JSON w tym folderze!",
                    "Brak wynikow", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int bledy = 0;
            foreach (string plikJson in plikiJson)
            {
                try
                {
                    string zawartosc = File.ReadAllText(plikJson);
                    WynikPary wynik = JsonConvert.DeserializeObject<WynikPary>(zawartosc);

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

            string komunikat = $"Wczytano {wczytaneWyniki.Count} wynikow.";
            if (bledy > 0)
                komunikat += $"\nPominieto {bledy} uszkodzonych plikow.";

            MessageBox.Show(komunikat, "Wczytywanie zakonczone",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // ── Zapis CSV / JSON ─────────────────────────────────────────────────
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

        // ── Logi ──────────────────────────────────────────────────────────────
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