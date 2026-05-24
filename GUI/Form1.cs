using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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
        private int _licznikZadan = -1;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _selectionCts;

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hWnd, int wMsg, bool wParam, int lParam);
        const int WM_SETREDRAW = 11;

        private int SlotowNaSerwer => (int)numSloty.Value;
        private int TimeoutMs => (int)numTimeout.Value * 1000;
        private int PortSerwera => (int)numPort.Value;

        // ── Connection Pool ───────────────────────────────────────────────────
        private sealed class TcpConnectionPool : IDisposable
        {
            private readonly string _host;
            private readonly int _port;
            private readonly int _timeoutMs;
            private readonly ConcurrentBag<TcpClient> _idle = new ConcurrentBag<TcpClient>();
            private int _total = 0;

            public TcpConnectionPool(string host, int port, int timeoutMs = 300_000)
            { _host = host; _port = port; _timeoutMs = timeoutMs; }

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
                client.NoDelay = true;
                client.ReceiveTimeout = _timeoutMs;
                client.SendTimeout = _timeoutMs;
                client.Connect(_host, _port);
                return client;
            }

            public void Return(TcpClient client, bool wasError = false)
            {
                if (wasError || !IsAlive(client)) { client.Close(); Interlocked.Decrement(ref _total); return; }
                _idle.Add(client);
            }

            private static bool IsAlive(TcpClient c)
            {
                try { return c != null && c.Connected && c.Client != null && c.Client.Connected; }
                catch { return false; }
            }

            public void Dispose()
            {
                while (_idle.TryTake(out var c)) try { c.Close(); } catch { }
            }
        }

        private static readonly ConcurrentDictionary<string, TcpConnectionPool> _pools
            = new ConcurrentDictionary<string, TcpConnectionPool>();

        private TcpConnectionPool GetPool(string host, int port)
            => _pools.GetOrAdd($"{host}:{port}", _ => new TcpConnectionPool(host, port, TimeoutMs));

        private void ResetPools()
        {
            foreach (var pool in _pools.Values) pool.Dispose();
            _pools.Clear();
        }

        // ── Model danych ──────────────────────────────────────────────────────
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

        // ── Konstruktor ───────────────────────────────────────────────────────
        public Form1()
        {
            InitializeComponent();

            listPary.SelectedIndexChanged += ListPary_SelectedIndexChanged;
            listPary.DrawMode = DrawMode.OwnerDrawFixed;
            listPary.DrawItem += ListPary_DrawItem;

            timerInterfejsu = new Timer();
            timerInterfejsu.Interval = 1000;
            timerInterfejsu.Tick += (s, e) => lblTimer.Text = $"Czas: {stoper.Elapsed:hh\\:mm\\:ss}";

            numProg.ValueChanged += (s, e) => OdswiezHeatmape();

            var tip = new ToolTip { AutoPopDelay = 8000, InitialDelay = 400 };
            tip.SetToolTip(numN, "Liczba słów w jednym n-gramie.\nWiększe n → mniej fałszywych trafień, ale wykrywa tylko dłuższe fragmenty.");
            tip.SetToolTip(numProg, "Minimalny Jaccard (%) uznawany za plagiat.\nPary powyżej progu oznaczane kolorem i wykrzyknikiem.");
            tip.SetToolTip(numSloty, "Ile równoległych połączeń TCP na jeden serwer.\nZwiększ przy szybkiej sieci i mocnych serwerach.");
            tip.SetToolTip(numTimeout, "Timeout połączenia TCP w sekundach.\nZwiększ przy wolnej sieci lub dużych plikach.");
            tip.SetToolTip(numPort, "Port TCP serwerów analizy (domyślnie 8001).");
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            _cts?.Cancel();
            _selectionCts?.Cancel();
            ResetPools();
            _heatmapaBitmap?.Dispose();
        }

        // ── Wybór plików ──────────────────────────────────────────────────────
        private void btnWybierzPliki_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.Multiselect = true;
                dialog.Title = "Wybierz pliki do analizy";
                dialog.Filter = "Wszystkie pliki (*.*)|*.*";
                if (dialog.ShowDialog() != DialogResult.OK) return;
                _wybranePliki = new List<string>(dialog.FileNames);
                lblWybranePliki.Text = $"Wybrano {_wybranePliki.Count} plików";
            }
        }

        // ── Kolorowanie listy ─────────────────────────────────────────────────
        private void ListPary_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= wczytaneWyniki.Count) return;
            WynikPary wynik = wczytaneWyniki[e.Index];
            double jaccard = wynik.jaccard;
            double prog = (double)numProg.Value;
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            Color tlo = selected ? Color.FromArgb(210, 230, 255) : Color.White;
            using (var tloB = new SolidBrush(tlo)) e.Graphics.FillRectangle(tloB, e.Bounds);
            Color kolorTekstu;
            if (jaccard >= prog)
            {
                double t = Math.Min((jaccard - prog) / Math.Max(100.0 - prog, 1.0), 1.0);
                kolorTekstu = Color.FromArgb(200, (int)(80 * (1.0 - t)), 0);
            }
            else kolorTekstu = Color.FromArgb(0, 130, 0);
            string tekst = Path.GetFileName(wynik.plikA) + " vs " + Path.GetFileName(wynik.plikB)
                         + "  —  " + jaccard.ToString("F2") + "%" + (jaccard >= prog ? "  ⚑" : "");
            using (var tekstB = new SolidBrush(kolorTekstu))
                e.Graphics.DrawString(tekst, e.Font, tekstB, e.Bounds.X + 4, e.Bounds.Y + 2);
            e.DrawFocusRectangle();
        }

        // ── Stan UI ───────────────────────────────────────────────────────────
        private void UstawTrybAnalizy(bool analizaTrwa)
        {
            btnAnalyzuj.Enabled = !analizaTrwa;
            btnWybierzPliki.Enabled = !analizaTrwa;
            btnWczytaj.Enabled = !analizaTrwa;
            btnAnuluj.Visible = analizaTrwa;
            numN.Enabled = !analizaTrwa;
            numProg.Enabled = !analizaTrwa;
            numSloty.Enabled = !analizaTrwa;
            numTimeout.Enabled = !analizaTrwa;
            numPort.Enabled = !analizaTrwa;
        }

        private void SetStatus(string tekst, int min, int max)
        {
            lblStatus.Text = tekst;
            lblStatus.ForeColor = Color.DimGray;
            progressBar.Minimum = min;
            progressBar.Maximum = Math.Max(max, 1);
            progressBar.Value = 0;
        }

        private void FinalizujAnulowanie()
        {
            stoper.Stop(); timerInterfejsu.Stop();
            lblStatus.Text = "Anulowano."; lblStatus.ForeColor = Color.DimGray;
            progressBar.Value = 0; UstawTrybAnalizy(false);
        }

        private void btnAnuluj_Click(object sender, EventArgs e)
        {
            _cts?.Cancel(); lblStatus.Text = "Anulowanie...";
        }

        // ── Wybór pary z listy ────────────────────────────────────────────────
        private async void ListPary_SelectedIndexChanged(object sender, EventArgs e)
        {
            _selectionCts?.Cancel();
            _selectionCts = new CancellationTokenSource();
            var token = _selectionCts.Token;
            int indeks = listPary.SelectedIndex;
            if (indeks < 0 || indeks >= wczytaneWyniki.Count) return;
            WynikPary wynik = wczytaneWyniki[indeks];
            lblNazwaA.Text = Path.GetFileName(wynik.plikA) + $"  (A→B: {wynik.aDoB:F2}%)";
            lblNazwaB.Text = Path.GetFileName(wynik.plikB) + $"  (B→A: {wynik.bDoA:F2}%)";
            rtbPlikA.Text = "Wczytywanie i renderowanie...";
            rtbPlikB.Text = "Wczytywanie i renderowanie...";
            string sciezkaA = wynik.plikA, sciezkaB = wynik.plikB;
            var zakresy1 = wynik.zakresy1; var zakresy2 = wynik.zakresy2;
            try
            {
                var (rtfA, rtfB) = await Task.Run<(string, string)>(() =>
                {
                    string tA = File.Exists(sciezkaA) ? File.ReadAllText(sciezkaA) : $"[Brak pliku: {sciezkaA}]";
                    if (token.IsCancellationRequested) return (null, null);
                    string rA = GenerujRtfZPodswietleniem(tA, zakresy1, token);
                    if (token.IsCancellationRequested) return (null, null);
                    string tB = File.Exists(sciezkaB) ? File.ReadAllText(sciezkaB) : $"[Brak pliku: {sciezkaB}]";
                    if (token.IsCancellationRequested) return (null, null);
                    string rB = GenerujRtfZPodswietleniem(tB, zakresy2, token);
                    return (rA, rB);
                }, token);
                if (!token.IsCancellationRequested && rtfA != null && rtfB != null)
                { rtbPlikA.Rtf = rtfA; rtbPlikB.Rtf = rtfB; }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { ZapiszLog($"[UI ERROR] {ex.Message}"); }
        }

        private string GenerujRtfZPodswietleniem(string tekst, List<Zakres> zakresy, CancellationToken token)
        {
            if (string.IsNullOrEmpty(tekst)) return string.Empty;
            var skonsolidowane = new List<Zakres>();
            if (zakresy != null && zakresy.Count > 0)
            {
                var pos = zakresy.Where(z => z.od < z.do_).OrderBy(z => z.od).ToList();
                if (pos.Count > 0)
                {
                    Zakres akt = new Zakres { od = pos[0].od, do_ = pos[0].do_ };
                    for (int i = 1; i < pos.Count; i++)
                    {
                        if (token.IsCancellationRequested) return null;
                        if (pos[i].od <= akt.do_) { if (pos[i].do_ > akt.do_) akt.do_ = pos[i].do_; }
                        else { skonsolidowane.Add(akt); akt = new Zakres { od = pos[i].od, do_ = pos[i].do_ }; }
                    }
                    skonsolidowane.Add(akt);
                }
            }
            var sb = new StringBuilder();
            sb.Append(@"{\rtf1\ansi\ansicpg1250\deff0\nouicompat{\fonttbl{\f0\fnil\fcharset238 Consolas;}}");
            sb.Append(@"{\colortbl ;\red255\green235\blue80;}");
            sb.Append(@"\viewkind4\uc1\f0\fs18 ");
            int lastIdx = 0, textLen = tekst.Length;
            foreach (var z in skonsolidowane)
            {
                if (token.IsCancellationRequested) return null;
                int od = Math.Max(0, Math.Min(z.od, textLen));
                int do_ = Math.Max(od, Math.Min(z.do_, textLen));
                if (od > lastIdx) AppendEscapedRtf(sb, tekst, lastIdx, od);
                if (do_ > od) { sb.Append(@"\highlight1 "); AppendEscapedRtf(sb, tekst, od, do_); sb.Append(@"\highlight0 "); }
                lastIdx = do_;
            }
            if (lastIdx < textLen) AppendEscapedRtf(sb, tekst, lastIdx, textLen);
            sb.Append("}");
            return sb.ToString();
        }

        private void AppendEscapedRtf(StringBuilder sb, string text, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                char c = text[i];
                switch (c)
                {
                    case '\\': sb.Append(@"\\"); break;
                    case '{': sb.Append(@"\{"); break;
                    case '}': sb.Append(@"\}"); break;
                    case '\n': sb.Append(@"\par "); break;
                    case '\r': break;
                    default:
                        if (c > 127) sb.Append(@"\u").Append((int)c).Append('?');
                        else sb.Append(c);
                        break;
                }
            }
        }

        // ── Główna analiza ────────────────────────────────────────────────────
        private async void btnAnalyzuj_Click(object sender, EventArgs e)
        {
            if (_wybranePliki.Count < 2)
            { MessageBox.Show("Wybierz co najmniej 2 pliki!", "Brak plików", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            var adresy = txtAdresy.Lines.Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (adresy.Count == 0)
            { MessageBox.Show("Podaj co najmniej jeden adres serwera!", "Brak serwerów", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

            int n = (int)numN.Value;
            double prog = (double)numProg.Value;
            int port = PortSerwera, sloty = SlotowNaSerwer;
            ResetPools();

            var posortowane = _wybranePliki.Where(f => File.Exists(f)).OrderByDescending(f => new FileInfo(f).Length).ToList();
            var pary = GenerujPary(posortowane);
            UstawTrybAnalizy(true);
            wczytaneWyniki.Clear(); listPary.Items.Clear();
            stoper.Restart(); timerInterfejsu.Start();
            _licznikZadan = -1;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            SetStatus("Faza 1/2: Przesyłanie plików na serwery...", 0, posortowane.Count * adresy.Count);
            var fileIds = new Dictionary<string, Dictionary<string, string>>();
            foreach (var adres in adresy) fileIds[adres] = new Dictionary<string, string>();
            int uploadWykonane = 0; bool uploadOK = true;

            try
            {
                await Task.Run(() =>
                {
                    var uploadTasks = new List<Task>();
                    foreach (var adres in adresy)
                        foreach (var sciezka in posortowane)
                        {
                            if (token.IsCancellationRequested) break;
                            string aL = adres, sL = sciezka;
                            uploadTasks.Add(Task.Run(() =>
                            {
                                if (token.IsCancellationRequested) return;
                                string fileId = WyslijUpload(aL, port, sL);
                                if (fileId == null) { uploadOK = false; ZapiszLog($"[UPLOAD BLAD] {aL} - {Path.GetFileName(sL)}"); }
                                else { lock (fileIds) fileIds[aL][sL] = fileId; }
                                int done = Interlocked.Increment(ref uploadWykonane);
                                BeginInvoke(new Action(() =>
                                {
                                    progressBar.Value = Math.Min(done, progressBar.Maximum);
                                    lblStatus.Text = $"Faza 1/2: Przesłano {done}/{posortowane.Count * adresy.Count} plików...";
                                }));
                            }, token));
                        }
                    Task.WaitAll(uploadTasks.ToArray());
                }, token);
            }
            catch (OperationCanceledException) { FinalizujAnulowanie(); return; }

            if (!uploadOK)
                MessageBox.Show("Nie udało się przesłać niektórych plików.\nSprawdź errors.log.", "Błąd uploadu", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (token.IsCancellationRequested) { FinalizujAnulowanie(); return; }

            SetStatus("Faza 2/2: Porównywanie par...", 0, pary.Count);
            int wykonane = 0;
            var wynikiBag = new ConcurrentBag<WynikPary>();
            int liczbaSerwerow = adresy.Count;

            try
            {
                await Task.Run(() =>
                {
                    int maxWatkow = Math.Min(liczbaSerwerow * sloty, Math.Max(pary.Count, 1));
                    var opcje = new ParallelOptions { MaxDegreeOfParallelism = maxWatkow, CancellationToken = token };
                    Parallel.ForEach(pary, opcje, (para) =>
                    {
                        int idx = (int)((uint)Interlocked.Increment(ref _licznikZadan) % liczbaSerwerow);
                        var wynik = WyslijCompareFailover(adresy, fileIds, idx, port, para.Item1, para.Item2, n);
                        if (wynik != null) wynikiBag.Add(wynik);
                        int done = Interlocked.Increment(ref wykonane);
                        BeginInvoke(new Action(() =>
                        {
                            progressBar.Value = Math.Min(done, pary.Count);
                            lblStatus.Text = $"Faza 2/2: {Path.GetFileName(para.Item1)} vs {Path.GetFileName(para.Item2)}";
                        }));
                    });
                }, token);
            }
            catch (OperationCanceledException) { FinalizujAnulowanie(); return; }

            stoper.Stop(); timerInterfejsu.Stop();
            lblTimer.Text = $"Czas całkowity: {stoper.Elapsed:hh\\:mm\\:ss}";

            int oczekiwane = pary.Count, otrzymane = wynikiBag.Count;
            if (oczekiwane != otrzymane)
                MessageBox.Show($"Oczekiwano: {oczekiwane}\nOtrzymano: {otrzymane}\nNieudane: {oczekiwane - otrzymane}\n\nSprawdź errors.log.", "Uwaga", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            wczytaneWyniki = wynikiBag.OrderByDescending(w => w.jaccard).ToList();
            OdswiezListe();
            OdswiezHeatmape();

            int plagiatow = wczytaneWyniki.Count(w => w.jaccard >= prog);
            lblStatus.Text = $"Gotowe! Przeanalizowano {wczytaneWyniki.Count} par." + (plagiatow > 0 ? $"  Wykryto {plagiatow} plagiatów!" : "");
            lblStatus.ForeColor = plagiatow > 0 ? Color.Firebrick : Color.DimGray;
            UstawTrybAnalizy(false);
        }

        // ── Heatmapa — buforowana bitmap ──────────────────────────────────────
        private List<string> _heatmapaPliki;
        private (int cellSize, int labelW, int labelH, int n) _heatmapaParams;
        private Bitmap _heatmapaBitmap;

        private void OdswiezHeatmape()
        {
            // Zwolnij stary bufor
            _heatmapaBitmap?.Dispose();
            _heatmapaBitmap = null;

            panelHeatmapa.Paint -= PanelHeatmapa_Paint;
            panelHeatmapa.MouseClick -= PanelHeatmapa_MouseClick;
            panelHeatmapa.Controls.Clear();

            if (wczytaneWyniki.Count == 0)
            {
                lblHeatmapaInfo.Text = "Brak wyników do wyświetlenia.";
                panelHeatmapa.Controls.Add(lblHeatmapaInfo);
                return;
            }

            var pliki = wczytaneWyniki
                .SelectMany(w => new[] { w.plikA, w.plikB })
                .Distinct()
                .OrderBy(p => Path.GetFileName(p))
                .ToList();

            _heatmapaPliki = pliki;

            int n = pliki.Count;
            // Rozmiar komórki: min 24px, max 48px
            int cellSize = Math.Max(24, Math.Min(48, 800 / Math.Max(n, 1)));
            // Szerokość etykiet wierszy — bazujemy na najdłuższej nazwie pliku
            int maxChars = pliki.Max(p => Path.GetFileName(p).Length);
            int labelW = Math.Min(maxChars * 6 + 8, 180); // max 180px, ~6px/znak
            int labelH = 80; // wysokość obszaru na obrócone etykiety kolumn

            int bmpW = labelW + n * cellSize + 2;
            int bmpH = labelH + n * cellSize + 35; // +35 na legendę

            _heatmapaParams = (cellSize, labelW, labelH, n);

            // Renderuj całą heatmapę do bitmapy w tle
            Task.Run(() =>
            {
                var bmp = RenderujHeatmapeDoBitmapy(pliki, cellSize, labelW, labelH, n, bmpW, bmpH);
                BeginInvoke(new Action(() =>
                {
                    _heatmapaBitmap?.Dispose();
                    _heatmapaBitmap = bmp;
                    // AutoScrollMinSize = dokładny rozmiar bitmapy — bez marginesów
                    panelHeatmapa.AutoScrollMinSize = new Size(bmpW, bmpH);
                    panelHeatmapa.Paint += PanelHeatmapa_Paint;
                    panelHeatmapa.MouseClick += PanelHeatmapa_MouseClick;
                    panelHeatmapa.Invalidate();
                }));
            });
        }

        private Bitmap RenderujHeatmapeDoBitmapy(
            List<string> pliki, int cellSize, int labelW, int labelH, int n, int bmpW, int bmpH)
        {
            double prog = (double)numProg.Value;

            var lookup = new Dictionary<(string, string), double>();
            foreach (var w in wczytaneWyniki)
            {
                lookup[(w.plikA, w.plikB)] = w.jaccard;
                lookup[(w.plikB, w.plikA)] = w.jaccard;
            }

            var bmp = new Bitmap(bmpW, bmpH);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                var fontLabel = new Font("Segoe UI", 7f);
                var fontCell = new Font("Segoe UI", 6.5f);
                var sfCenter = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                var sfFar = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                var clrTekst = new SolidBrush(Color.FromArgb(50, 55, 70));
                var clrLabel = new SolidBrush(Color.FromArgb(60, 70, 90));

                // Komórki
                for (int row = 0; row < n; row++)
                {
                    for (int col = 0; col < n; col++)
                    {
                        int x = labelW + col * cellSize;
                        int y = labelH + row * cellSize;
                        var rect = new Rectangle(x, y, cellSize - 1, cellSize - 1);

                        Color bg; string tekst;
                        if (row == col) { bg = Color.FromArgb(210, 215, 225); tekst = "—"; }
                        else
                        {
                            string pA = pliki[row], pB = pliki[col];
                            if (lookup.TryGetValue((pA, pB), out double jac))
                            { bg = JaccardNaKolor(jac, prog); tekst = jac.ToString("F0") + "%"; }
                            else { bg = Color.FromArgb(240, 242, 245); tekst = "?"; }
                        }

                        using (var br = new SolidBrush(bg)) g.FillRectangle(br, rect);
                        g.DrawRectangle(Pens.LightGray, rect);
                        g.DrawString(tekst, fontCell, clrTekst, rect, sfCenter);
                    }
                }

                // Etykiety wierszy
                for (int i = 0; i < n; i++)
                {
                    int y = labelH + i * cellSize;
                    var lr = new Rectangle(2, y, labelW - 4, cellSize - 1);
                    g.DrawString(Path.GetFileName(pliki[i]), fontLabel, clrLabel, lr, sfFar);
                }

                // Etykiety kolumn — obrócone -45°
                for (int i = 0; i < n; i++)
                {
                    int x = labelW + i * cellSize + cellSize / 2;
                    int y = labelH - 4;
                    g.TranslateTransform(x, y);
                    g.RotateTransform(-45);
                    g.DrawString(Path.GetFileName(pliki[i]), fontLabel, clrLabel, 0, -fontLabel.Height);
                    g.ResetTransform();
                }

                // Legenda
                int legX = labelW;
                int legY = labelH + n * cellSize + 6;
                int legW = Math.Min(n * cellSize, 300);
                int legH = 10;

                for (int i = 0; i <= legW; i++)
                {
                    double jac = (double)i / legW * 100.0;
                    using (var p = new Pen(JaccardNaKolor(jac, prog)))
                        g.DrawLine(p, legX + i, legY, legX + i, legY + legH);
                }
                g.DrawRectangle(Pens.Gray, new Rectangle(legX, legY, legW, legH));
                g.DrawString("0%", fontLabel, clrLabel, legX, legY + legH + 1);
                g.DrawString($"próg: {prog:F0}%", fontLabel, clrLabel, legX + legW / 2 - 20, legY + legH + 1);
                g.DrawString("100%", fontLabel, clrLabel, legX + legW - 22, legY + legH + 1);

                fontLabel.Dispose(); fontCell.Dispose();
                sfCenter.Dispose(); sfFar.Dispose();
                clrTekst.Dispose(); clrLabel.Dispose();
            }
            return bmp;
        }

        // Paint — tylko blit bitmapy, zero obliczeń
        private void PanelHeatmapa_Paint(object sender, PaintEventArgs e)
        {
            if (_heatmapaBitmap == null) return;
            int ox = panelHeatmapa.AutoScrollPosition.X;
            int oy = panelHeatmapa.AutoScrollPosition.Y;
            e.Graphics.DrawImage(_heatmapaBitmap, ox, oy);
        }

        private Color JaccardNaKolor(double jaccard, double prog)
        {
            if (jaccard < prog)
            {
                double t = jaccard / Math.Max(prog, 1.0);
                return Color.FromArgb((int)(255 - t * 60), (int)(255 - t * 30), (int)(255 - t * 80));
            }
            else
            {
                double t = Math.Min((jaccard - prog) / Math.Max(100.0 - prog, 1.0), 1.0);
                return Color.FromArgb((int)(255 - t * 55), Math.Max((int)(160 - t * 140), 0), Math.Max((int)(80 - t * 70), 0));
            }
        }

        private void PanelHeatmapa_MouseClick(object sender, MouseEventArgs e)
        {
            if (_heatmapaPliki == null || _heatmapaBitmap == null) return;
            var (cellSize, labelW, labelH, n) = _heatmapaParams;
            int ox = panelHeatmapa.AutoScrollPosition.X;
            int oy = panelHeatmapa.AutoScrollPosition.Y;
            int col = (e.X - ox - labelW) / cellSize;
            int row = (e.Y - oy - labelH) / cellSize;
            if (row < 0 || row >= n || col < 0 || col >= n || row == col) return;
            string pA = _heatmapaPliki[row], pB = _heatmapaPliki[col];
            int idx = wczytaneWyniki.FindIndex(w =>
                (w.plikA == pA && w.plikB == pB) || (w.plikA == pB && w.plikB == pA));
            if (idx >= 0)
            {
                tabGlowny.SelectedTab = tabPorownanie;
                listPary.SelectedIndex = idx;
                listPary.TopIndex = idx;
            }
        }

        // ── Sieć ─────────────────────────────────────────────────────────────
        private string WyslijUpload(string adres, int port, string sciezka)
        {
            try
            {
                byte[] dane = File.ReadAllBytes(sciezka);
                string nazwa = Path.GetFileName(sciezka);
                using (var klient = new TcpClient())
                {
                    klient.NoDelay = true; klient.ReceiveTimeout = TimeoutMs; klient.SendTimeout = TimeoutMs;
                    klient.Connect(adres, port);
                    using (var stream = klient.GetStream())
                    using (var writer = new BinaryWriter(stream))
                    using (var reader = new BinaryReader(stream))
                    {
                        writer.Write((byte)0x01); writer.Write(nazwa); writer.Write((long)dane.Length);
                        int wyslane = 0;
                        while (wyslane < dane.Length)
                        { int p = Math.Min(65536, dane.Length - wyslane); writer.Write(dane, wyslane, p); wyslane += p; }
                        writer.Flush();
                        string fileId = reader.ReadString(); bool wCache = reader.ReadBoolean();
                        return fileId;
                    }
                }
            }
            catch (Exception ex) { ZapiszLog($"[UPLOAD ERROR] {adres}:{port} - {Path.GetFileName(sciezka)} - {ex.Message}"); return null; }
        }

        private WynikPary WyslijCompareFailover(List<string> adresy, Dictionary<string, Dictionary<string, string>> fileIds,
            int startIdx, int port, string plikA, string plikB, int n)
        {
            int maxProby = Math.Max(adresy.Count * 3, 5);
            for (int i = 0; i < maxProby; i++)
            {
                if (_cts?.IsCancellationRequested == true) return null;
                int aktIdx = (startIdx + i) % adresy.Count;
                string adres = adresy[aktIdx];
                if (!fileIds[adres].TryGetValue(plikA, out string idA) || !fileIds[adres].TryGetValue(plikB, out string idB))
                { ZapiszLog($"[FAILOVER] Serwer {adres} nie ma file_id dla {Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)}."); continue; }
                var wynik = WyslijCompareZPuli(adres, port, idA, idB, plikA, plikB, n);
                if (wynik != null) return wynik;
                ZapiszLog($"[FAILOVER] COMPARE na {adres}:{port} zawiodło.");
                Thread.Sleep(300);
            }
            return null;
        }

        private WynikPary WyslijCompareZPuli(string adres, int port, string fileIdA, string fileIdB,
            string plikA, string plikB, int n)
        {
            var pool = GetPool(adres, port);
            TcpClient klient = null; bool blad = false;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                klient = pool.Checkout();
                long swConnect = sw.ElapsedMilliseconds;
                var stream = klient.GetStream();
                var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
                var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
                writer.Write((byte)0x02); writer.Write(fileIdA); writer.Write(fileIdB); writer.Write(n); writer.Write(1);
                writer.Flush();
                double jaccard = reader.ReadDouble();
                if (jaccard < 0) { ZapiszLog($"[COMPARE ERROR] Serwer {adres} nie rozpoznał file_id."); blad = true; return null; }
                double aDoB = reader.ReadDouble(), bDoA = reader.ReadDouble();
                int lz1 = reader.ReadInt32();
                var z1 = new List<Zakres>(lz1);
                for (int i = 0; i < lz1; i++) z1.Add(new Zakres { od = reader.ReadInt32(), do_ = reader.ReadInt32() });
                int lz2 = reader.ReadInt32();
                var z2 = new List<Zakres>(lz2);
                for (int i = 0; i < lz2; i++) z2.Add(new Zakres { od = reader.ReadInt32(), do_ = reader.ReadInt32() });
                string csv = reader.ReadString(), json = reader.ReadString();
                long czasSerwera = reader.ReadInt64();
                sw.Stop();
                ZapiszLog($"[TIMING] {Path.GetFileName(plikA)} vs {Path.GetFileName(plikB)} @ {adres} — connect:{swConnect}ms całość:{sw.ElapsedMilliseconds}ms serwer:{czasSerwera}ms");
                ZapiszCSVLokalnie(plikA, plikB, csv);
                ZapiszJSONLokalnie(plikA, plikB, json);
                return new WynikPary { plikA = plikA, plikB = plikB, jaccard = jaccard, aDoB = aDoB, bDoA = bDoA, zakresy1 = z1, zakresy2 = z2 };
            }
            catch (SocketException ex) { blad = true; ZapiszLog($"[SOCKET ERROR] {adres}:{port} — {ex.Message}"); return null; }
            catch (IOException ex) { blad = true; ZapiszLog($"[IO ERROR] {adres}:{port} — {ex.Message}"); return null; }
            catch (Exception ex) { blad = true; ZapiszLog($"[ERROR] {adres}:{port} — {ex.Message}"); return null; }
            finally { if (klient != null) pool.Return(klient, wasError: blad); }
        }

        private List<(string, string)> GenerujPary(List<string> pliki)
        {
            var pary = new List<(string, string)>();
            for (int i = 0; i < pliki.Count; i++)
                for (int j = i + 1; j < pliki.Count; j++)
                    pary.Add((pliki[i], pliki[j]));
            return pary;
        }

        private void OdswiezListe()
        {
            listPary.BeginUpdate();
            listPary.Items.Clear();
            foreach (var wynik in wczytaneWyniki)
                listPary.Items.Add(Path.GetFileName(wynik.plikA) + " vs " + Path.GetFileName(wynik.plikB));
            listPary.EndUpdate();
            lblLicznikPar.Text = $"Par: {wczytaneWyniki.Count}"
                + (wczytaneWyniki.Count > 0 ? $"  |  Plagiatów: {wczytaneWyniki.Count(w => w.jaccard >= (double)numProg.Value)}" : "");
        }

        // ── Wczytaj z folderu ─────────────────────────────────────────────────
        private async void btnWczytaj_Click(object sender, EventArgs e)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Wybierz folder z wynikami (Raporty)";
                if (dialog.ShowDialog() != DialogResult.OK) return;
                string folder = dialog.SelectedPath;
                lblFolder.Text = folder;
                UstawTrybAnalizy(true);
                lblStatus.Text = "Wczytywanie wyników...";
                var wyniki = new List<WynikPary>(); int bledy = 0;
                string[] plikiJson = Directory.GetFiles(folder, "*.json");
                if (plikiJson.Length == 0)
                {
                    MessageBox.Show("Nie znaleziono żadnych plików JSON w tym folderze!", "Brak wyników", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    UstawTrybAnalizy(false); lblStatus.Text = ""; return;
                }
                await Task.Run(() =>
                {
                    foreach (string pj in plikiJson)
                    {
                        try
                        {
                            var w = JsonConvert.DeserializeObject<WynikPary>(File.ReadAllText(pj));
                            if (w?.plikA != null && w?.plikB != null) wyniki.Add(w); else bledy++;
                        }
                        catch { bledy++; }
                    }
                    wyniki.Sort((a, b) => b.jaccard.CompareTo(a.jaccard));
                });
                wczytaneWyniki = wyniki;
                OdswiezListe();
                OdswiezHeatmape();
                UstawTrybAnalizy(false);
                lblStatus.Text = $"Wczytano {wczytaneWyniki.Count} wyników." + (bledy > 0 ? $"  Pominięto {bledy} uszkodzonych." : "");
            }
        }

        // ── Zapis ─────────────────────────────────────────────────────────────
        private void ZapiszCSVLokalnie(string plikA, string plikB, string csv)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 4);
                string path = Path.Combine("Raporty", $"{Path.GetFileNameWithoutExtension(plikA)}_VS_{Path.GetFileNameWithoutExtension(plikB)}_{DateTime.Now:yyyyMMdd_HHmmss}_{suffix}.csv");
                lock (_plikLock) File.WriteAllText(path, csv);
            }
            catch { }
        }

        private void ZapiszJSONLokalnie(string plikA, string plikB, string json)
        {
            try
            {
                Directory.CreateDirectory("Raporty");
                string suffix = Guid.NewGuid().ToString("N").Substring(0, 4);
                string path = Path.Combine("Raporty", $"{Path.GetFileNameWithoutExtension(plikA)}_VS_{Path.GetFileNameWithoutExtension(plikB)}_{DateTime.Now:yyyyMMdd_HHmmss}_{suffix}.json");
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
                lock (_logLock) File.AppendAllText(plik, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {komunikat}{Environment.NewLine}");
            }
            catch { }
        }
    }
}