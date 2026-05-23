using System;
using System.Collections.Concurrent;
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
        // ── Stałe konfiguracyjne ──────────────────────────────────────────────
        private const int SLOTOW_NA_SERWER = 6;     // podniesione z 4
        private const int TIMEOUT_MS = 300_000;
        private const int MAX_ROWNOLEGLOSCI_UPLOAD = 16; // upload równoległy

        // ── Stan aplikacji ────────────────────────────────────────────────────
        private System.Diagnostics.Stopwatch stoper = new System.Diagnostics.Stopwatch();
        private Timer timerInterfejsu;
        private List<WynikPary> wczytaneWyniki = new List<WynikPary>();
        private List<string> _wybranePliki = new List<string>();

        private static readonly object _plikLock = new object();
        private static readonly object _logLock = new object();

        /// <summary>
        /// Mapa: ścieżka_pliku → (adres serwera, file_id)
        /// Uwaga: po zmianie architektury (upload do wszystkich serwerów)
        /// każdy plik ma JEDEN wpis – serwer wyznaczony przez affinity hash,
        /// ale jest też dostępny na pozostałych (dla 0x02 zamiast 0x03).
        /// </summary>
        private ConcurrentDictionary<string, (string Adres, int Port, string FileId)> _lokalizacjePlikow
            = new ConcurrentDictionary<string, (string, int, string)>();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam);
        const int WM_SETREDRAW = 11;

        // ════════════════════════════════════════════════════════════════════
        // Connection Pool
        // ════════════════════════════════════════════════════════════════════
        private sealed class TcpConnectionPool : IDisposable
        {
            private readonly string _host;
            private readonly int _port;
            private readonly int _timeoutMs;
            private readonly ConcurrentBag<TcpClient> _idle = new ConcurrentBag<TcpClient>();
            private int _total = 0;

            public TcpConnectionPool(string host, int port, int timeoutMs = 300_000)
            {
                _host = host;
                _port = port;
                _timeoutMs = timeoutMs;
            }

            public TcpClient Checkout()
            {
                while (_idle.TryTake(out TcpClient? existing))
                {
                    if (IsAlive(existing)) return existing;
                    existing.Close();
                    Interlocked.Decrement(ref _total);
                }
                Interlocked.Increment(ref _total);
                var client = new TcpClient();
                client.ReceiveTimeout = _timeoutMs;
                client.SendTimeout = _timeoutMs;
                client.Connect(_host, _port);
                return client;
            }

            public void Return(TcpClient client, bool wasError = false)
            {
                if (wasError || !IsAlive(client))
                {
                    client.Close();
                    Interlocked.Decrement(ref _total);
                    return;
                }
                _idle.Add(client);
            }

            private static bool IsAlive(TcpClient c)
            {
                try
                {
                    if (c == null || !c.Connected || c.Client == null || !c.Client.Connected)
                        return false;
                    if (c.Client.Poll(0, SelectMode.SelectRead))
                    {
                        byte[] test = new byte[1];
                        if (c.Client.Receive(test, SocketFlags.Peek) == 0) return false;
                    }
                    return true;
                }
                catch { return false; }
            }

            public void Dispose()
            {
                while (_idle.TryTake(out var c))
                    try { c.Close(); } catch { }
            }
        }

        private static readonly ConcurrentDictionary<string, TcpConnectionPool> _pools
            = new ConcurrentDictionary<string, TcpConnectionPool>();

        private TcpConnectionPool GetPool(string host, int port)
            => _pools.GetOrAdd($"{host}:{port}", _ => new TcpConnectionPool(host, port, TIMEOUT_MS));

        // ════════════════════════════════════════════════════════════════════
        // Modele danych
        // ════════════════════════════════════════════════════════════════════
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

        private class Zakres { public int od { get; set; } public int do_ { get; set; } }

        // ════════════════════════════════════════════════════════════════════
        // Konstruktor / Inicjalizacja
        // ════════════════════════════════════════════════════════════════════
        public Form1()
        {
            InitializeComponent();
            listPary.SelectedIndexChanged += ListPary_SelectedIndexChanged;
            listPary.DrawMode = DrawMode.OwnerDrawFixed;
            listPary.DrawItem += ListPary_DrawItem;

            timerInterfejsu = new Timer();
            timerInterfejsu.Interval = 1000;
            timerInterfejsu.Tick += (s, e) => lblTimer.Text = $"Czas: {stoper.Elapsed:hh\\:mm\\:ss}";
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            foreach (var pool in _pools.Values) pool.Dispose();
            _pools.Clear();
        }

        // ════════════════════════════════════════════════════════════════════
        // Affinity hash – ten sam plik zawsze trafia na ten sam serwer
        // (używany tylko do wyznaczenia "głównego" serwera do COMPARE)
        // ════════════════════════════════════════════════════════════════════
        private static int PobierzIndeksSerwera(string sciezka, int liczbaSerwerow)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFileName(sciezka)));
            return Math.Abs(BitConverter.ToInt32(hash, 0)) % liczbaSerwerow;
        }

        // ════════════════════════════════════════════════════════════════════
        // GŁÓWNA ANALIZA
        // ════════════════════════════════════════════════════════════════════
        private async void btnAnalyzuj_Click(object sender, EventArgs e)
        {
            if (_wybranePliki.Count < 2)
            {
                MessageBox.Show("Wybierz co najmniej 2 pliki!", "Brak plikow",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Parsuj adresy i porty z formatu "adres:port" lub samego "adres"
            var serwery = ParseujSerwery(txtAdresy.Lines);
            if (serwery.Count == 0)
            {
                MessageBox.Show("Podaj co najmniej jeden adres serwera!", "Brak serwerow",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int n = (int)numN.Value;
            int grainSize = (int)numGrainSize.Value;
            double prog = (double)numProg.Value;

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

            // ── FAZA 1: Upload każdego pliku na WSZYSTKIE serwery ─────────────
            // Eliminuje cross-server compare (0x03) – wszystkie pary używają 0x02
            lblStatus.Text = "Faza 1/2: Przesylanie plikow na serwery...";
            progressBar.Minimum = 0;
            progressBar.Maximum = posortowane.Count * serwery.Count;
            progressBar.Value = 0;
            _lokalizacjePlikow.Clear();

            int uploadWykonane = 0;
            bool uploadOK = true;

            await Task.Run(() =>
            {
                // Ograniczamy równoległość uploadu (nie zalewamy sieci)
                var uploadSem = new SemaphoreSlim(MAX_ROWNOLEGLOSCI_UPLOAD, MAX_ROWNOLEGLOSCI_UPLOAD);
                var uploadTasks = new List<Task>();

                foreach (var sciezka in posortowane)
                {
                    // Wyznacz "główny" serwer dla tego pliku (do compare)
                    int affinityIdx = PobierzIndeksSerwera(sciezka, serwery.Count);
                    string affinityAdres = serwery[affinityIdx].adres;
                    int affinityPort = serwery[affinityIdx].port;

                    foreach (var (adres, port) in serwery)
                    {
                        string s = sciezka;
                        string a = adres;
                        int p = port;
                        bool isAffinity = (a == affinityAdres && p == affinityPort);

                        uploadTasks.Add(Task.Run(async () =>
                        {
                            await uploadSem.WaitAsync();
                            try
                            {
                                // Upload używa teraz connection pool
                                string? fileId = WyslijUploadZPuli(a, p, s);

                                if (fileId == null)
                                {
                                    uploadOK = false;
                                    ZapiszLog($"[UPLOAD BLAD] {a}:{p} - {Path.GetFileName(s)}");
                                }
                                else if (isAffinity)
                                {
                                    // Rejestruj tylko raz – dla serwera affinity (do compare)
                                    _lokalizacjePlikow[s] = (a, p, fileId);
                                }
                            }
                            finally { uploadSem.Release(); }

                            int done = Interlocked.Increment(ref uploadWykonane);
                            BeginInvoke(new Action(() =>
                            {
                                progressBar.Value = Math.Min(done, progressBar.Maximum);
                                lblStatus.Text = $"Faza 1/2: Upload {done}/{posortowane.Count * serwery.Count}...";
                            }));
                        }));
                    }
                }

                var sw1 = System.Diagnostics.Stopwatch.StartNew();
                Task.WaitAll(uploadTasks.ToArray());
                sw1.Stop();
                ZapiszLog($"[FAZA1] Upload: {sw1.ElapsedMilliseconds}ms ({posortowane.Count} plikow × {serwery.Count} serwerow)");
            });

            if (!uploadOK)
                MessageBox.Show("Nie udalo sie przeslac niektorych plikow.\nSprawdz errors.log.",
                    "Blad uploadu", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            // ── FAZA 2: Porównywanie (wyłącznie 0x02 – brak cross-server) ────
            lblStatus.Text = "Faza 2/2: Porownywanie par...";
            progressBar.Minimum = 0;
            progressBar.Maximum = pary.Count;
            progressBar.Value = 0;

            int wykonane = 0;
            var wynikiBag = new ConcurrentBag<WynikPary>();

            await Task.Run(() =>
            {
                // Maksymalna równoległość: wszystkie sloty wszystkich serwerów,
                // ale nie więcej niż liczba par
                int maxWatkow = Math.Min(
                    serwery.Count * SLOTOW_NA_SERWER,
                    Math.Max(pary.Count, serwery.Count * SLOTOW_NA_SERWER));
                maxWatkow = Math.Max(maxWatkow, 4); // minimum 4 wątki

                var opcje = new ParallelOptions { MaxDegreeOfParallelism = maxWatkow };
                var sw2 = System.Diagnostics.Stopwatch.StartNew();

                Parallel.ForEach(pary, opcje, (para) =>
                {
                    if (!_lokalizacjePlikow.TryGetValue(para.Item1, out var locA) ||
                        !_lokalizacjePlikow.TryGetValue(para.Item2, out var locB))
                    {
                        ZapiszLog($"[SKIP] Brak lokalizacji dla pary: {Path.GetFileName(para.Item1)} / {Path.GetFileName(para.Item2)}");
                        return;
                    }

                    WynikPary? wynik = null;

                    for (int attempt = 0; attempt < 3 && wynik == null; attempt++)
                    {
                        if (attempt > 0) Thread.Sleep(300 * attempt);

                        // ZAWSZE 0x02 – oba pliki są na obu serwerach po Fazie 1
                        // Wybieramy serwer A (affinity pliku A)
                        wynik = WyslijCompareZPuli(
                            locA.Adres, locA.Port,
                            locA.FileId, locB.FileId,
                            para.Item1, para.Item2,
                            n, grainSize);
                    }

                    if (wynik != null)
                        wynikiBag.Add(wynik);
                    else
                        ZapiszLog($"[BLAD] Porownanie zawiodlo: {Path.GetFileName(para.Item1)} vs {Path.GetFileName(para.Item2)}");

                    int done = Interlocked.Increment(ref wykonane);
                    BeginInvoke(new Action(() =>
                    {
                        progressBar.Value = Math.Min(done, pary.Count);
                        lblStatus.Text = $"Faza 2/2: {Path.GetFileName(para.Item1)} vs {Path.GetFileName(para.Item2)}";
                    }));
                });

                sw2.Stop();
                ZapiszLog($"[FAZA2] {pary.Count} par: {sw2.ElapsedMilliseconds}ms ({serwery.Count} serwerow, {maxWatkow} watkow)");
            });

            stoper.Stop();
            timerInterfejsu.Stop();
            lblTimer.Text = $"Czas calkowity: {stoper.Elapsed:hh\\:mm\\:ss}";

            if (pary.Count != wynikiBag.Count)
                MessageBox.Show(
                    $"Oczekiwano: {pary.Count}\nOtrzymano: {wynikiBag.Count}\nSprawdz errors.log.",
                    "Uwaga - brakujace wyniki", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            wczytaneWyniki = wynikiBag.OrderByDescending(w => w.jaccard).ToList();
            OdswiezListe();

            int plagiatow = wczytaneWyniki.Count(w => w.jaccard >= prog);
            lblStatus.Text = $"Gotowe! {wczytaneWyniki.Count} par." +
                             (plagiatow > 0 ? $" Wykryto {plagiatow} plagiatow!" : "");

            btnAnalyzuj.Enabled = true;
            btnWybierzPliki.Enabled = true;
        }

        // ════════════════════════════════════════════════════════════════════
        // Parsowanie adresów serwerów z pola tekstowego
        // Format: "adres" lub "adres:port"  (domyślny port: 8001)
        // ════════════════════════════════════════════════════════════════════
        private static List<(string adres, int port)> ParseujSerwery(string[] linie)
        {
            var wynik = new List<(string, int)>();
            foreach (string linia in linie)
            {
                string l = linia.Trim();
                if (l.Length == 0) continue;

                int port = 8001;
                string adres = l;

                int dwukropek = l.LastIndexOf(':');
                if (dwukropek > 0 && int.TryParse(l[(dwukropek + 1)..], out int p))
                {
                    adres = l[..dwukropek];
                    port = p;
                }

                if (!wynik.Any(x => x.Item1 == adres && x.Item2 == port))
                    wynik.Add((adres, port));
            }
            return wynik;
        }

        // ════════════════════════════════════════════════════════════════════
        // UPLOAD przez Connection Pool
        // (NAPRAWA: poprzednia wersja tworzyła nowy TcpClient za każdym razem)
        // ════════════════════════════════════════════════════════════════════
        private string? WyslijUploadZPuli(string adres, int port, string sciezka)
        {
            var pool = GetPool(adres, port);
            TcpClient? klient = null;
            bool blad = false;

            try
            {
                byte[] dane = File.ReadAllBytes(sciezka);
                string nazwa = Path.GetFileName(sciezka);

                klient = pool.Checkout();
                var stream = klient.GetStream();
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

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
                reader.ReadBoolean(); // juz_w_cache (informacja dla logów)
                return fileId;
            }
            catch (Exception ex)
            {
                blad = true;
                ZapiszLog($"[UPLOAD ERROR] {adres}:{port} - {Path.GetFileName(sciezka)} - {ex.Message}");
                return null;
            }
            finally
            {
                if (klient != null) pool.Return(klient, wasError: blad);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // COMPARE (0x02) – standardowe porównanie dwóch plików na tym samym serwerze
        // ════════════════════════════════════════════════════════════════════
        private WynikPary? WyslijCompareZPuli(
            string adres, int port,
            string fileIdA, string fileIdB,
            string plikA, string plikB,
            int n, int grainSize)
        {
            var pool = GetPool(adres, port);
            TcpClient? klient = null;
            bool blad = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                klient = pool.Checkout();
                var stream = klient.GetStream();
                using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

                writer.Write((byte)0x02);
                writer.Write(fileIdA);
                writer.Write(fileIdB);
                writer.Write(n);
                writer.Write(grainSize);
                writer.Flush();

                return OdczytajWynik(reader, adres, plikA, plikB, sw, "0x02");
            }
            catch (Exception ex)
            {
                blad = true;
                ZapiszLog($"[ERROR COMPARE] {adres}:{port} — {ex.Message}");
                return null;
            }
            finally
            {
                if (klient != null) pool.Return(klient, wasError: blad);
            }
        }

        // ════════════════════════════════════════════════════════════════════
        // Odczyt wyniku (wspólny dla wszystkich typów compare)
        // ════════════════════════════════════════════════════════════════════
        private WynikPary? OdczytajWynik(
            BinaryReader reader,
            string adres, string plikA, string plikB,
            System.Diagnostics.Stopwatch sw, string typ)
        {
            double jaccard = reader.ReadDouble();
            if (jaccard < 0)
            {
                ZapiszLog($"[COMPARE ERROR] Serwer {adres} odrzucił file_id dla {Path.GetFileName(plikA)}.");
                return null;
            }

            double aDoB = reader.ReadDouble();
            double bDoA = reader.ReadDouble();

            int liczba1 = reader.ReadInt32();
            var zakresy1 = new List<Zakres>(liczba1);
            for (int i = 0; i < liczba1; i++)
                zakresy1.Add(new Zakres { od = reader.ReadInt32(), do_ = reader.ReadInt32() });

            int liczba2 = reader.ReadInt32();
            var zakresy2 = new List<Zakres>(liczba2);
            for (int i = 0; i < liczba2; i++)
                zakresy2.Add(new Zakres { od = reader.ReadInt32(), do_ = reader.ReadInt32() });

            string csv = reader.ReadString();
            string json = reader.ReadString();
            reader.ReadInt64(); // czasMs (zużyty przez serwer)

            ZapiszCSVLokalnie(plikA, plikB, csv);
            ZapiszJSONLokalnie(plikA, plikB, json);
            sw.Stop();
            ZapiszLog($"[CZAS] {Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)} @ {adres} [{typ}] — {sw.ElapsedMilliseconds}ms");

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

        // ════════════════════════════════════════════════════════════════════
        // Generowanie par
        // ════════════════════════════════════════════════════════════════════
        private List<(string, string)> GenerujPary(List<string> pliki)
        {
            var pary = new List<(string, string)>(pliki.Count * (pliki.Count - 1) / 2);
            for (int i = 0; i < pliki.Count; i++)
                for (int j = i + 1; j < pliki.Count; j++)
                    pary.Add((pliki[i], pliki[j]));
            return pary;
        }

        // ════════════════════════════════════════════════════════════════════
        // UI: wybór plików
        // ════════════════════════════════════════════════════════════════════
        private void btnWybierzPliki_Click(object sender, EventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Wybierz pliki do analizy",
                Filter = "Wszystkie pliki (*.*)|*.*"
            };
            if (dialog.ShowDialog() != DialogResult.OK) return;
            _wybranePliki = new List<string>(dialog.FileNames);
            lblWybranePliki.Text = $"Wybrano {_wybranePliki.Count} plikow";
        }

        // ════════════════════════════════════════════════════════════════════
        // UI: lista par – rysowanie
        // ════════════════════════════════════════════════════════════════════
        private void ListPary_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= wczytaneWyniki.Count) return;
            WynikPary wynik = wczytaneWyniki[e.Index];
            double prog = (double)numProg.Value;
            double jaccard = wynik.jaccard;

            Color tlo = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                ? Color.FromArgb(220, 220, 220) : Color.White;
            using (var b = new SolidBrush(tlo))
                e.Graphics.FillRectangle(b, e.Bounds);

            double t = Math.Min((jaccard - prog) / Math.Max(100.0 - prog, 1.0), 1.0);
            Color kolorTekstu = jaccard >= prog
                ? Color.FromArgb(255, (int)(140 * (1.0 - t)), 0)
                : Color.FromArgb(0, 140, 0);

            string tekst = $"{Path.GetFileName(wynik.plikA)} vs {Path.GetFileName(wynik.plikB)}" +
                           $"  —  {jaccard:F2}%{(jaccard >= prog ? " !" : "")}";
            using (var b = new SolidBrush(kolorTekstu))
                e.Graphics.DrawString(tekst, e.Font, b, e.Bounds.X + 2, e.Bounds.Y + 2);
            e.DrawFocusRectangle();
        }

        // ════════════════════════════════════════════════════════════════════
        // UI: lista par – zaznaczenie
        // ════════════════════════════════════════════════════════════════════
        private void ListPary_SelectedIndexChanged(object sender, EventArgs e)
        {
            int indeks = listPary.SelectedIndex;
            if (indeks < 0 || indeks >= wczytaneWyniki.Count) return;

            WynikPary wynik = wczytaneWyniki[indeks];
            lblNazwaA.Text = $"{Path.GetFileName(wynik.plikA)} (A→B: {wynik.aDoB:F2}%)";
            lblNazwaB.Text = $"{Path.GetFileName(wynik.plikB)} (B→A: {wynik.bDoA:F2}%)";
            rtbPlikA.Text = "Wczytywanie...";
            rtbPlikB.Text = "Wczytywanie...";

            string sciezkaA = wynik.plikA, sciezkaB = wynik.plikB;
            var zakresy1 = wynik.zakresy1;
            var zakresy2 = wynik.zakresy2;

            Task.Run(() =>
            {
                string tekstA = WczytajTekstLokalnie(sciezkaA);
                string tekstB = WczytajTekstLokalnie(sciezkaB);
                this.Invoke((Action)(() =>
                {
                    rtbPlikA.Text = tekstA;
                    rtbPlikB.Text = tekstB;
                    PodswietlFragmenty(rtbPlikA, zakresy1);
                    PodswietlFragmenty(rtbPlikB, zakresy2);
                }));
            });
        }

        private string WczytajTekstLokalnie(string sciezka)
        {
            try { return File.Exists(sciezka) ? File.ReadAllText(sciezka) : $"[Plik niedostepny: {sciezka}]"; }
            catch (Exception ex) { return $"[Blad odczytu: {ex.Message}]"; }
        }

        // ════════════════════════════════════════════════════════════════════
        // UI: podświetlanie fragmentów
        // ════════════════════════════════════════════════════════════════════
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
                        if (od + dlugosc > rtb.TextLength) dlugosc = rtb.TextLength - od;
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

        // ════════════════════════════════════════════════════════════════════
        // Wczytywanie wyników z folderu
        // ════════════════════════════════════════════════════════════════════
        private void btnWczytaj_Click(object sender, EventArgs e)
        {
            var dialog = new FolderBrowserDialog { Description = "Wybierz folder z wynikami (Raporty)" };
            if (dialog.ShowDialog() != DialogResult.OK) return;

            string folder = dialog.SelectedPath;
            lblFolder.Text = folder;
            wczytaneWyniki.Clear();
            listPary.Items.Clear();

            string[] plikiJson = Directory.GetFiles(folder, "*.json");
            if (plikiJson.Length == 0)
            {
                MessageBox.Show("Nie znaleziono plikow JSON!", "Brak",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int bledy = 0;
            foreach (string plikJson in plikiJson)
            {
                try
                {
                    var wynik = JsonConvert.DeserializeObject<WynikPary>(File.ReadAllText(plikJson));
                    if (wynik?.plikA == null || wynik.plikB == null) { bledy++; continue; }
                    wczytaneWyniki.Add(wynik);
                }
                catch { bledy++; }
            }

            wczytaneWyniki.Sort((a, b) => b.jaccard.CompareTo(a.jaccard));
            OdswiezListe();
            MessageBox.Show(
                $"Wczytano {wczytaneWyniki.Count} wynikow." + (bledy > 0 ? $"\nPominieto {bledy}." : ""),
                "Zakonczono", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OdswiezListe()
        {
            listPary.BeginUpdate();
            listPary.Items.Clear();
            foreach (var w in wczytaneWyniki)
                listPary.Items.Add($"{Path.GetFileName(w.plikA)} vs {Path.GetFileName(w.plikB)}");
            listPary.EndUpdate();
        }

        // ════════════════════════════════════════════════════════════════════
        // Zapis plików lokalnych
        // ════════════════════════════════════════════════════════════════════
        private string GenerujUnikalnaSciezke(string folder, string fA, string fB, string ext)
        {
            string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string uniq = Guid.NewGuid().ToString("N")[..4];
            return Path.Combine(folder, $"{fA}_VS_{fB}_{ts}_{uniq}.{ext}");
        }

        private void ZapiszCSVLokalnie(string plikA, string plikB, string csv)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string path = GenerujUnikalnaSciezke("Raporty",
                    Path.GetFileNameWithoutExtension(plikA),
                    Path.GetFileNameWithoutExtension(plikB), "csv");
                lock (_plikLock) File.WriteAllText(path, csv);
            }
            catch { }
        }

        private void ZapiszJSONLokalnie(string plikA, string plikB, string json)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string path = GenerujUnikalnaSciezke("Raporty",
                    Path.GetFileNameWithoutExtension(plikA),
                    Path.GetFileNameWithoutExtension(plikB), "json");
                lock (_plikLock) File.WriteAllText(path, json);
            }
            catch { }
        }

        private void ZapiszLog(string komunikat)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string plik = Path.Combine("Raporty", "errors.log");
                lock (_logLock) File.AppendAllText(plik,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {komunikat}\r\n");
            }
            catch { }
        }
    }
}