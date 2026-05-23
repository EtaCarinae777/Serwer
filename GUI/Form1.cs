using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography; // Dodane dla Hashowania MD5
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
        private const int SLOTOW_NA_SERWER = 4;
        const int TIMEOUT_MS = 300_000;

        // POPRAWKA: Mapa przechowująca informację, na który serwer powędrował dany plik
        private ConcurrentDictionary<string, (string Adres, string FileId)> _lokalizacjePlikow =
            new ConcurrentDictionary<string, (string, string)>();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam);
        const int WM_SETREDRAW = 11;

        // ── Connection Pool (zagnieżdżone) ────────────────────────────────────
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
                while (_idle.TryTake(out TcpClient existing))
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
            => _pools.GetOrAdd($"{host}:{port}",
                _ => new TcpConnectionPool(host, port, TIMEOUT_MS));
        // ─────────────────────────────────────────────────────────────────────

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

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            foreach (var pool in _pools.Values) pool.Dispose();
            _pools.Clear();
        }

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

        private void ListPary_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= wczytaneWyniki.Count) return;
            WynikPary wynik = wczytaneWyniki[e.Index];
            double jaccard = wynik.jaccard;
            double prog = (double)numProg.Value;
            Color tlo = (e.State & DrawItemState.Selected) == DrawItemState.Selected
                ? Color.FromArgb(220, 220, 220) : Color.White;
            using (SolidBrush tloB = new SolidBrush(tlo))
                e.Graphics.FillRectangle(tloB, e.Bounds);
            Color kolorTekstu = jaccard >= prog
                ? Color.FromArgb(255, (int)(140 * (1.0 - Math.Min((jaccard - prog) / (100.0 - prog), 1.0))), 0)
                : Color.FromArgb(0, 140, 0);
            string tekst = Path.GetFileName(wynik.plikA) + " vs " + Path.GetFileName(wynik.plikB) +
                           "  -  " + jaccard.ToString("F2") + "%" + (jaccard >= prog ? " !" : "");
            using (SolidBrush tekstB = new SolidBrush(kolorTekstu))
                e.Graphics.DrawString(tekst, e.Font, tekstB, e.Bounds.X + 2, e.Bounds.Y + 2);
            e.DrawFocusRectangle();
        }

        // POPRAWKA: Funkcja wyznaczająca serwer na bazie zawartości (nazwy) pliku.
        // Gwarantuje, że ten sam plik zawsze trafi na ten sam serwer.
        private int PobierzIndeksSerwera(string sciezka, int liczbaSerwerow)
        {
            using (var md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFileName(sciezka)));
                return Math.Abs(BitConverter.ToInt32(hash, 0)) % liczbaSerwerow;
            }
        }

        private async void btnAnalyzuj_Click(object sender, EventArgs e)
        {
            if (_wybranePliki.Count < 2)
            {
                MessageBox.Show("Wybierz co najmniej 2 pliki!", "Brak plikow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            List<string> adresy = txtAdresy.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (adresy.Count == 0)
            {
                MessageBox.Show("Podaj co najmniej jeden adres serwera!", "Brak serwerow", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int n = (int)numN.Value;
            int grainSize = (int)numGrainSize.Value;
            double prog = (double)numProg.Value;
            int port = 8001;
            var posortowane = _wybranePliki.Where(f => File.Exists(f)).OrderByDescending(f => new FileInfo(f).Length).ToList();
            var pary = GenerujPary(posortowane);

            btnAnalyzuj.Enabled = false; btnWybierzPliki.Enabled = false;
            wczytaneWyniki.Clear();
            listPary.Items.Clear();
            stoper.Restart(); timerInterfejsu.Start();

            // ── FAZA 1: upload z Koligacją (Affinity) ───────────────────────
            lblStatus.Text = "Faza 1/2: Przesylanie plikow na serwery...";
            progressBar.Minimum = 0; progressBar.Maximum = posortowane.Count; progressBar.Value = 0;
            _lokalizacjePlikow.Clear();

            int uploadWykonane = 0;
            bool uploadOK = true;
            await Task.Run(() =>
            {
                var uploadTasks = new List<Task>();
                foreach (var sciezka in posortowane)
                {
                    string sciezkaLocal = sciezka;
                    uploadTasks.Add(Task.Run(() =>
                    {
                        // Wybieramy TYLKO JEDEN najodpowiedniejszy serwer dla pliku
                        int idx = PobierzIndeksSerwera(sciezkaLocal, adresy.Count);
                        string adresTarget = adresy[idx];

                        string fileId = WyslijUpload(adresTarget, port, sciezkaLocal);
                        if (fileId == null)
                        {
                            uploadOK = false;
                            ZapiszLog($"[UPLOAD BLAD] {adresTarget} - {Path.GetFileName(sciezkaLocal)}");
                        }
                        else
                        {
                            _lokalizacjePlikow[sciezkaLocal] = (adresTarget, fileId);
                        }

                        int done = Interlocked.Increment(ref uploadWykonane);
                        BeginInvoke(new Action(() => {
                            progressBar.Value = Math.Min(done, progressBar.Maximum);
                            lblStatus.Text = $"Faza 1/2: Przeslano {done}/{posortowane.Count} plikow...";
                        }));
                    }));
                }

                var swFaza1 = System.Diagnostics.Stopwatch.StartNew();
                Task.WaitAll(uploadTasks.ToArray());
                swFaza1.Stop();
                ZapiszLog($"[FAZA1] Upload: {swFaza1.ElapsedMilliseconds}ms ({posortowane.Count} plikow)");
            });

            if (!uploadOK) MessageBox.Show("Nie udalo sie przeslac niektorych plikow.\nSprawdz errors.log.", "Blad uploadu", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            // ── FAZA 2: Porównywanie ──────────────────────────────────────────
            lblStatus.Text = "Faza 2/2: Porownywanie par...";
            progressBar.Minimum = 0; progressBar.Maximum = pary.Count; progressBar.Value = 0;

            int wykonane = 0;
            var wynikiBag = new ConcurrentBag<WynikPary>();
            await Task.Run(() =>
            {
                int maxWatkow = Math.Min(adresy.Count * SLOTOW_NA_SERWER, pary.Count);
                var opcje = new ParallelOptions { MaxDegreeOfParallelism = maxWatkow };
                var swFaza2 = System.Diagnostics.Stopwatch.StartNew();

                Parallel.ForEach(pary, opcje, (para) =>
                {
                    // Upewniamy się, że oba pliki przeszły Upload
                    if (!_lokalizacjePlikow.TryGetValue(para.Item1, out var locA) ||
                        !_lokalizacjePlikow.TryGetValue(para.Item2, out var locB)) return;

                    WynikPary wynik = null;

                    // Retries wbudowane bezpośrednio do zadania (zamiast szukać w ciemno innych serwerów)
                    for (int i = 0; i < 3; i++)
                    {
                        if (locA.Adres == locB.Adres)
                        {
                            // Pliki są na tym samym serwerze. Standardowe zapytanie 0x02.
                            wynik = WyslijCompareZPuli(locA.Adres, port, locA.FileId, locB.FileId, para.Item1, para.Item2, n, grainSize);
                        }
                        else
                        {
                            // Pliki leżą na różnych serwerach!
                            // Wysyłamy plik B na serwer A do porównania (0x03).
                            wynik = WyslijCompareCrossServerZPuli(locA.Adres, port, locA.FileId, para.Item1, para.Item2, n, grainSize);
                        }

                        if (wynik != null) break;
                        Thread.Sleep(300); // Backoff przed ponowną próbą
                    }

                    if (wynik != null) wynikiBag.Add(wynik);
                    else ZapiszLog($"[BLAD] Porownanie zawiodlo ostatecznie dla: {Path.GetFileName(para.Item1)} vs {Path.GetFileName(para.Item2)}");

                    int done = Interlocked.Increment(ref wykonane);
                    BeginInvoke(new Action(() => {
                        progressBar.Value = Math.Min(done, pary.Count);
                        lblStatus.Text = $"Faza 2/2: {Path.GetFileName(para.Item1)} vs {Path.GetFileName(para.Item2)}";
                    }));
                });
                swFaza2.Stop();
                ZapiszLog($"[FAZA2] Porownywanie {pary.Count} par: {swFaza2.ElapsedMilliseconds}ms ({adresy.Count} serwerow)");
            });

            stoper.Stop(); timerInterfejsu.Stop();
            lblTimer.Text = $"Czas calkowity: {stoper.Elapsed:hh\\:mm\\:ss}";
            if (pary.Count != wynikiBag.Count)
                MessageBox.Show($"Oczekiwano: {pary.Count}\nOtrzymano: {wynikiBag.Count}\nSprawdz errors.log.", "Uwaga - brakujace wyniki", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            wczytaneWyniki = wynikiBag.OrderByDescending(w => w.jaccard).ToList();
            OdswiezListe();

            int plagiatow = wczytaneWyniki.Count(w => w.jaccard >= prog);
            lblStatus.Text = $"Gotowe! Przeanalizowano {wczytaneWyniki.Count} par."
                + (plagiatow > 0 ? $" Wykryto {plagiatow} plagiatow!" : "");
            btnAnalyzuj.Enabled = true; btnWybierzPliki.Enabled = true;
        }

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

        // Standardowe COMPARE (0x02) - Pliki są na tym samym serwerze
        private WynikPary WyslijCompareZPuli(string adres, int port, string fileIdA, string fileIdB, string plikA, string plikB, int n, int grainSize)
        {
            var pool = GetPool(adres, port);
            TcpClient klient = null;
            bool blad = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                klient = pool.Checkout();
                var stream = klient.GetStream();
                var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                writer.Write((byte)0x02);
                writer.Write(fileIdA); writer.Write(fileIdB);
                writer.Write(n); writer.Write(grainSize);
                writer.Flush();

                return OdczytajWynik(reader, adres, plikA, plikB, sw, "(lokalne)");
            }
            catch (Exception ex)
            {
                blad = true;
                ZapiszLog($"[ERROR] {adres}:{port} — {ex.Message}"); return null;
            }
            finally
            {
                if (klient != null) pool.Return(klient, wasError: blad);
            }
        }

        // NOWE: CROSS-COMPARE (0x03) - Przesyła zawartość pliku B "w locie" do serwera A
        private WynikPary WyslijCompareCrossServerZPuli(string adres, int port, string fileIdA, string plikA, string plikB, int n, int grainSize)
        {
            var pool = GetPool(adres, port);
            TcpClient klient = null;
            bool blad = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                klient = pool.Checkout();
                var stream = klient.GetStream();
                var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                byte[] daneB = File.ReadAllBytes(plikB);
                string nazwaB = Path.GetFileName(plikB);

                writer.Write((byte)0x03); // Nowa komenda!
                writer.Write(fileIdA);
                writer.Write(nazwaB);
                writer.Write((long)daneB.Length);
                writer.Write(daneB);
                // Wysyłamy plik B prosto w strumień
                writer.Write(n);
                writer.Write(grainSize);
                writer.Flush();

                return OdczytajWynik(reader, adres, plikA, plikB, sw, "(cross)");
            }
            catch (Exception ex)
            {
                blad = true;
                ZapiszLog($"[ERROR CROSS] {adres}:{port} — {ex.Message}"); return null;
            }
            finally
            {
                if (klient != null) pool.Return(klient, wasError: blad);
            }
        }

        // Pomocnicza metoda (odczyt zawsze jest taki sam)
        private WynikPary OdczytajWynik(BinaryReader reader, string adres, string plikA, string plikB, System.Diagnostics.Stopwatch sw, string typ)
        {
            double jaccard = reader.ReadDouble();
            if (jaccard < 0)
            {
                ZapiszLog($"[COMPARE ERROR] Serwer {adres} odrzucił file_id dla {Path.GetFileName(plikA)}."); return null;
            }

            double aDoB = reader.ReadDouble(); double bDoA = reader.ReadDouble();
            int liczbaZakresow1 = reader.ReadInt32();
            var zakresy1 = new List<Zakres>(liczbaZakresow1);
            for (int i = 0; i < liczbaZakresow1; i++) zakresy1.Add(new Zakres { od = reader.ReadInt32(), do_ = reader.ReadInt32() });
            int liczbaZakresow2 = reader.ReadInt32();
            var zakresy2 = new List<Zakres>(liczbaZakresow2);
            for (int i = 0; i < liczbaZakresow2; i++) zakresy2.Add(new Zakres { od = reader.ReadInt32(), do_ = reader.ReadInt32() });
            string csv = reader.ReadString();
            string json = reader.ReadString();
            reader.ReadInt64();

            ZapiszCSVLokalnie(plikA, plikB, csv); ZapiszJSONLokalnie(plikA, plikB, json);
            sw.Stop();
            ZapiszLog($"[CZAS] {Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)} @ {adres} {typ} — {sw.ElapsedMilliseconds}ms");

            return new WynikPary { plikA = plikA, plikB = plikB, jaccard = jaccard, aDoB = aDoB, bDoA = bDoA, zakresy1 = zakresy1, zakresy2 = zakresy2 };
        }

        private List<(string, string)> GenerujPary(List<string> pliki)
        {
            var pary = new List<(string, string)>();
            for (int i = 0; i < pliki.Count; i++)
                for (int j = i + 1; j < pliki.Count; j++) pary.Add((pliki[i], pliki[j]));
            return pary;
        }

        private void OdswiezListe()
        {
            listPary.BeginUpdate();
            listPary.Items.Clear();
            foreach (var wynik in wczytaneWyniki) listPary.Items.Add(Path.GetFileName(wynik.plikA) + " vs " + Path.GetFileName(wynik.plikB));
            listPary.EndUpdate();
        }

        private void ListPary_SelectedIndexChanged(object sender, EventArgs e)
        {
            int indeks = listPary.SelectedIndex;
            if (indeks < 0 || indeks >= wczytaneWyniki.Count) return;

            WynikPary wynik = wczytaneWyniki[indeks];
            lblNazwaA.Text = Path.GetFileName(wynik.plikA) + $" (A→B: {wynik.aDoB:F2}%)";
            lblNazwaB.Text = Path.GetFileName(wynik.plikB) + $" (B→A: {wynik.bDoA:F2}%)";
            rtbPlikA.Text = "Wczytywanie..."; rtbPlikB.Text = "Wczytywanie...";

            string sciezkaA = wynik.plikA, sciezkaB = wynik.plikB;
            var zakresy1 = wynik.zakresy1;
            var zakresy2 = wynik.zakresy2;
            Task.Run(() =>
            {
                string tekstA = WczytajTekstLokalnie(sciezkaA), tekstB = WczytajTekstLokalnie(sciezkaB);
                this.Invoke((Action)(() => {
                    rtbPlikA.Text = tekstA; rtbPlikB.Text = tekstB;
                    PodswietlFragmenty(rtbPlikA, zakresy1); PodswietlFragmenty(rtbPlikB, zakresy2);
                }));
            });
        }

        private string WczytajTekstLokalnie(string sciezka)
        {
            try
            {
                if (File.Exists(sciezka)) return File.ReadAllText(sciezka);
                return $"[Plik niedostepny: {sciezka}]";
            }
            catch (Exception ex)
            {
                return $"[Blad odczytu: {ex.Message}]";
            }
        }

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
                        int od = zakres.od, dlugosc = zakres.do_ - zakres.od;
                        if (od < 0 || od >= rtb.TextLength) continue;
                        if (od + dlugosc > rtb.TextLength) dlugosc = rtb.TextLength - od;
                        if (dlugosc <= 0) continue;
                        rtb.Select(od, dlugosc); rtb.SelectionBackColor = Color.Yellow;
                    }
                }
                rtb.Select(0, 0);
            }
            finally
            {
                SendMessage(rtb.Handle, WM_SETREDRAW, true, 0); rtb.Invalidate();
            }
        }

        private void btnWczytaj_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "Wybierz folder z wynikami (Raporty)";
            if (dialog.ShowDialog() != DialogResult.OK) return;

            string folder = dialog.SelectedPath; lblFolder.Text = folder;
            wczytaneWyniki.Clear();
            listPary.Items.Clear();
            string[] plikiJson = Directory.GetFiles(folder, "*.json");

            if (plikiJson.Length == 0)
            {
                MessageBox.Show("Nie znaleziono plikow JSON!", "Brak", MessageBoxButtons.OK, MessageBoxIcon.Information); return;
            }

            int bledy = 0;
            foreach (string plikJson in plikiJson)
            {
                try
                {
                    WynikPary wynik = JsonConvert.DeserializeObject<WynikPary>(File.ReadAllText(plikJson));
                    if (wynik == null || wynik.plikA == null || wynik.plikB == null)
                    {
                        bledy++; continue;
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
            MessageBox.Show($"Wczytano {wczytaneWyniki.Count} wynikow." + (bledy > 0 ? $"\nPominieto {bledy}." : ""), "Zakonczono", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void ZapiszCSVLokalnie(string plikA, string plikB, string csv)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string path = Path.Combine("Raporty", $"{Path.GetFileNameWithoutExtension(plikA)}_VS_{Path.GetFileNameWithoutExtension(plikB)}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 4)}.csv"); lock (_plikLock) File.WriteAllText(path, csv);
            }
            catch { }
        }

        private void ZapiszJSONLokalnie(string plikA, string plikB, string json)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string path = Path.Combine("Raporty", $"{Path.GetFileNameWithoutExtension(plikA)}_VS_{Path.GetFileNameWithoutExtension(plikB)}_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 4)}.json"); lock (_plikLock) File.WriteAllText(path, json);
            }
            catch { }
        }

        private void ZapiszLog(string komunikat)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string plik = Path.Combine("Raporty", "errors.log"); lock (_logLock) File.AppendAllText(plik, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {komunikat}\r\n");
            }
            catch { }
        }
    }
}