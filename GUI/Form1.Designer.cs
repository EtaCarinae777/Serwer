namespace GUI
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            // Kontrolki
            this.panelLewy = new System.Windows.Forms.Panel();
            this.panelPlikA = new System.Windows.Forms.Panel();
            this.panelPlikB = new System.Windows.Forms.Panel();

            this.btnWybierzPliki = new System.Windows.Forms.Button();
            this.lblWybranePliki = new System.Windows.Forms.Label();

            this.lblAdresy = new System.Windows.Forms.Label();
            this.txtAdresy = new System.Windows.Forms.TextBox();

            this.lblSeparator1 = new System.Windows.Forms.Label();

            // Parametry analizy
            this.lblN = new System.Windows.Forms.Label();
            this.numN = new System.Windows.Forms.NumericUpDown();
            this.lblProg = new System.Windows.Forms.Label();
            this.numProg = new System.Windows.Forms.NumericUpDown();

            this.lblSeparator2 = new System.Windows.Forms.Label();

            // Parametry zaawansowane
            this.lblSloty = new System.Windows.Forms.Label();
            this.numSloty = new System.Windows.Forms.NumericUpDown();
            this.lblTimeout = new System.Windows.Forms.Label();
            this.numTimeout = new System.Windows.Forms.NumericUpDown();
            this.lblPort = new System.Windows.Forms.Label();
            this.numPort = new System.Windows.Forms.NumericUpDown();

            this.lblSeparator3 = new System.Windows.Forms.Label();

            this.btnAnalyzuj = new System.Windows.Forms.Button();
            this.btnAnuluj = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblTimer = new System.Windows.Forms.Label();

            this.lblSeparator4 = new System.Windows.Forms.Label();

            this.lblFolder = new System.Windows.Forms.Label();
            this.btnWczytaj = new System.Windows.Forms.Button();

            this.lblPary = new System.Windows.Forms.Label();
            this.lblLicznikPar = new System.Windows.Forms.Label();
            this.listPary = new System.Windows.Forms.ListBox();

            this.lblNazwaA = new System.Windows.Forms.Label();
            this.rtbPlikA = new System.Windows.Forms.RichTextBox();
            this.lblNazwaB = new System.Windows.Forms.Label();
            this.rtbPlikB = new System.Windows.Forms.RichTextBox();

            ((System.ComponentModel.ISupportInitialize)(this.numN)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numProg)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSloty)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTimeout)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).BeginInit();
            this.panelLewy.SuspendLayout();
            this.panelPlikA.SuspendLayout();
            this.panelPlikB.SuspendLayout();
            this.SuspendLayout();

            // ── Kolory motywu ────────────────────────────────────────────────
            var clrPanel = System.Drawing.Color.FromArgb(245, 247, 250);
            var clrSep = System.Drawing.Color.FromArgb(210, 215, 225);
            var clrLabel = System.Drawing.Color.FromArgb(60, 70, 90);
            var clrSubLabel = System.Drawing.Color.FromArgb(120, 130, 150);
            var clrAccent = System.Drawing.Color.FromArgb(37, 99, 235);
            var fontUI = new System.Drawing.Font("Segoe UI", 8.5f);
            var fontBold = new System.Drawing.Font("Segoe UI", 8.5f, System.Drawing.FontStyle.Bold);
            var fontSmall = new System.Drawing.Font("Segoe UI", 7.5f);
            var fontMono = new System.Drawing.Font("Consolas", 9f);

            // ================================================================
            // PANEL LEWY (szerokość 230px)
            // ================================================================
            this.panelLewy.Location = new System.Drawing.Point(0, 0);
            this.panelLewy.Size = new System.Drawing.Size(232, 620);
            this.panelLewy.Anchor = System.Windows.Forms.AnchorStyles.Top
                                     | System.Windows.Forms.AnchorStyles.Bottom
                                     | System.Windows.Forms.AnchorStyles.Left;
            this.panelLewy.BackColor = clrPanel;
            this.panelLewy.Name = "panelLewy";

            int lx = 10; // lewy margines wewnątrz panelu
            int lw = 210; // szerokość kontrolek
            int cy = 8;  // bieżąca pozycja Y

            // --- Wybór plików ---
            this.btnWybierzPliki.Location = new System.Drawing.Point(lx, cy);
            this.btnWybierzPliki.Size = new System.Drawing.Size(lw, 28);
            this.btnWybierzPliki.Text = "📂  Wybierz pliki do analizy...";
            this.btnWybierzPliki.Font = fontBold;
            this.btnWybierzPliki.UseVisualStyleBackColor = true;
            this.btnWybierzPliki.Click += new System.EventHandler(this.btnWybierzPliki_Click);

            cy += 32;
            this.lblWybranePliki.Location = new System.Drawing.Point(lx, cy);
            this.lblWybranePliki.Size = new System.Drawing.Size(lw, 16);
            this.lblWybranePliki.Text = "Nie wybrano plików";
            this.lblWybranePliki.Font = fontSmall;
            this.lblWybranePliki.ForeColor = clrSubLabel;

            cy += 22;
            // --- Serwery ---
            this.lblAdresy.Location = new System.Drawing.Point(lx, cy);
            this.lblAdresy.Size = new System.Drawing.Size(lw, 16);
            this.lblAdresy.Text = "Adresy serwerów (jeden na linię):";
            this.lblAdresy.Font = fontUI;
            this.lblAdresy.ForeColor = clrLabel;

            cy += 18;
            this.txtAdresy.Location = new System.Drawing.Point(lx, cy);
            this.txtAdresy.Size = new System.Drawing.Size(lw, 56);
            this.txtAdresy.Multiline = true;
            this.txtAdresy.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtAdresy.Text = "127.0.0.1";
            this.txtAdresy.Font = fontMono;
            this.txtAdresy.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

            cy += 62;
            // ── Separator ──
            this.lblSeparator1.Location = new System.Drawing.Point(lx, cy);
            this.lblSeparator1.Size = new System.Drawing.Size(lw, 1);
            this.lblSeparator1.BackColor = clrSep;

            cy += 7;
            // --- Parametry analizy ---
            var lblParTitle = new System.Windows.Forms.Label();
            lblParTitle.Location = new System.Drawing.Point(lx, cy);
            lblParTitle.Size = new System.Drawing.Size(lw, 16);
            lblParTitle.Text = "PARAMETRY ANALIZY";
            lblParTitle.Font = new System.Drawing.Font("Segoe UI", 7.5f, System.Drawing.FontStyle.Bold);
            lblParTitle.ForeColor = clrSubLabel;

            cy += 20;
            this.lblN.Location = new System.Drawing.Point(lx, cy);
            this.lblN.Size = new System.Drawing.Size(lw - 85, 16);
            this.lblN.Text = "Rozmiar n-gramów (n):";
            this.lblN.Font = fontUI;
            this.lblN.ForeColor = clrLabel;

            this.numN.Location = new System.Drawing.Point(lx + lw - 80, cy - 1);
            this.numN.Size = new System.Drawing.Size(80, 22);
            this.numN.Minimum = 1;
            this.numN.Maximum = 20;
            this.numN.Value = 4;
            this.numN.Font = fontUI;

            cy += 26;
            this.lblProg.Location = new System.Drawing.Point(lx, cy);
            this.lblProg.Size = new System.Drawing.Size(lw - 85, 16);
            this.lblProg.Text = "Próg plagiatu (%):";
            this.lblProg.Font = fontUI;
            this.lblProg.ForeColor = clrLabel;

            this.numProg.Location = new System.Drawing.Point(lx + lw - 80, cy - 1);
            this.numProg.Size = new System.Drawing.Size(80, 22);
            this.numProg.Minimum = 0;
            this.numProg.Maximum = 100;
            this.numProg.DecimalPlaces = 1;
            this.numProg.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            this.numProg.Value = new decimal(new int[] { 300, 0, 0, 65536 }); // 30.0
            this.numProg.Font = fontUI;

            cy += 30;
            // ── Separator ──
            this.lblSeparator2.Location = new System.Drawing.Point(lx, cy);
            this.lblSeparator2.Size = new System.Drawing.Size(lw, 1);
            this.lblSeparator2.BackColor = clrSep;

            cy += 7;
            // --- Parametry zaawansowane ---
            var lblAdvTitle = new System.Windows.Forms.Label();
            lblAdvTitle.Location = new System.Drawing.Point(lx, cy);
            lblAdvTitle.Size = new System.Drawing.Size(lw, 16);
            lblAdvTitle.Text = "ZAAWANSOWANE";
            lblAdvTitle.Font = new System.Drawing.Font("Segoe UI", 7.5f, System.Drawing.FontStyle.Bold);
            lblAdvTitle.ForeColor = clrSubLabel;

            cy += 20;
            this.lblSloty.Location = new System.Drawing.Point(lx, cy);
            this.lblSloty.Size = new System.Drawing.Size(lw - 85, 16);
            this.lblSloty.Text = "Połączeń na serwer:";
            this.lblSloty.Font = fontUI;
            this.lblSloty.ForeColor = clrLabel;

            this.numSloty.Location = new System.Drawing.Point(lx + lw - 80, cy - 1);
            this.numSloty.Size = new System.Drawing.Size(80, 22);
            this.numSloty.Minimum = 1;
            this.numSloty.Maximum = 32;
            this.numSloty.Value = 4;
            this.numSloty.Font = fontUI;

            cy += 26;
            this.lblTimeout.Location = new System.Drawing.Point(lx, cy);
            this.lblTimeout.Size = new System.Drawing.Size(lw - 85, 16);
            this.lblTimeout.Text = "Timeout (s):";
            this.lblTimeout.Font = fontUI;
            this.lblTimeout.ForeColor = clrLabel;

            this.numTimeout.Location = new System.Drawing.Point(lx + lw - 80, cy - 1);
            this.numTimeout.Size = new System.Drawing.Size(80, 22);
            this.numTimeout.Minimum = 10;
            this.numTimeout.Maximum = 3600;
            this.numTimeout.Increment = 30;
            this.numTimeout.Value = 300;
            this.numTimeout.Font = fontUI;

            cy += 26;
            this.lblPort.Location = new System.Drawing.Point(lx, cy);
            this.lblPort.Size = new System.Drawing.Size(lw - 85, 16);
            this.lblPort.Text = "Port serwera:";
            this.lblPort.Font = fontUI;
            this.lblPort.ForeColor = clrLabel;

            this.numPort.Location = new System.Drawing.Point(lx + lw - 80, cy - 1);
            this.numPort.Size = new System.Drawing.Size(80, 22);
            this.numPort.Minimum = 1;
            this.numPort.Maximum = 65535;
            this.numPort.Value = 8001;
            this.numPort.Font = fontUI;

            cy += 30;
            // ── Separator ──
            this.lblSeparator3.Location = new System.Drawing.Point(lx, cy);
            this.lblSeparator3.Size = new System.Drawing.Size(lw, 1);
            this.lblSeparator3.BackColor = clrSep;

            cy += 8;
            // --- Przycisk Analizuj ---
            this.btnAnalyzuj.Location = new System.Drawing.Point(lx, cy);
            this.btnAnalyzuj.Size = new System.Drawing.Size(lw, 32);
            this.btnAnalyzuj.Text = "▶  Analizuj";
            this.btnAnalyzuj.Font = new System.Drawing.Font("Segoe UI", 9.5f, System.Drawing.FontStyle.Bold);
            this.btnAnalyzuj.BackColor = clrAccent;
            this.btnAnalyzuj.ForeColor = System.Drawing.Color.White;
            this.btnAnalyzuj.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAnalyzuj.FlatAppearance.BorderSize = 0;
            this.btnAnalyzuj.UseVisualStyleBackColor = false;
            this.btnAnalyzuj.Click += new System.EventHandler(this.btnAnalyzuj_Click);

            cy += 36;
            this.btnAnuluj.Location = new System.Drawing.Point(lx, cy);
            this.btnAnuluj.Size = new System.Drawing.Size(lw, 24);
            this.btnAnuluj.Text = "✕  Anuluj";
            this.btnAnuluj.Font = fontBold;
            this.btnAnuluj.BackColor = System.Drawing.Color.FromArgb(220, 60, 60);
            this.btnAnuluj.ForeColor = System.Drawing.Color.White;
            this.btnAnuluj.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAnuluj.FlatAppearance.BorderSize = 0;
            this.btnAnuluj.UseVisualStyleBackColor = false;
            this.btnAnuluj.Visible = false;
            this.btnAnuluj.Click += new System.EventHandler(this.btnAnuluj_Click);

            cy += 30;
            this.progressBar.Location = new System.Drawing.Point(lx, cy);
            this.progressBar.Size = new System.Drawing.Size(lw, 12);
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;

            cy += 16;
            this.lblStatus.Location = new System.Drawing.Point(lx, cy);
            this.lblStatus.Size = new System.Drawing.Size(lw, 32);
            this.lblStatus.Font = fontSmall;
            this.lblStatus.ForeColor = clrSubLabel;

            cy += 36;
            this.lblTimer.Location = new System.Drawing.Point(lx, cy);
            this.lblTimer.Size = new System.Drawing.Size(lw, 16);
            this.lblTimer.Text = "Czas: 00:00:00";
            this.lblTimer.Font = fontBold;
            this.lblTimer.ForeColor = clrLabel;

            cy += 24;
            // ── Separator ──
            this.lblSeparator4.Location = new System.Drawing.Point(lx, cy);
            this.lblSeparator4.Size = new System.Drawing.Size(lw, 1);
            this.lblSeparator4.BackColor = clrSep;

            cy += 8;
            // --- Wczytaj z folderu ---
            this.lblFolder.Location = new System.Drawing.Point(lx, cy);
            this.lblFolder.Size = new System.Drawing.Size(lw, 16);
            this.lblFolder.Text = "Nie wybrano folderu";
            this.lblFolder.Font = fontSmall;
            this.lblFolder.ForeColor = clrSubLabel;

            cy += 20;
            this.btnWczytaj.Location = new System.Drawing.Point(lx, cy);
            this.btnWczytaj.Size = new System.Drawing.Size(lw, 26);
            this.btnWczytaj.Text = "📁  Wczytaj wyniki z folderu...";
            this.btnWczytaj.Font = fontUI;
            this.btnWczytaj.UseVisualStyleBackColor = true;
            this.btnWczytaj.Click += new System.EventHandler(this.btnWczytaj_Click);

            cy += 32;
            // --- Lista par ---
            this.lblPary.Location = new System.Drawing.Point(lx, cy);
            this.lblPary.Size = new System.Drawing.Size(80, 16);
            this.lblPary.Text = "Lista par:";
            this.lblPary.Font = fontBold;
            this.lblPary.ForeColor = clrLabel;

            this.lblLicznikPar.Location = new System.Drawing.Point(lx + 82, cy);
            this.lblLicznikPar.Size = new System.Drawing.Size(lw - 82, 16);
            this.lblLicznikPar.Text = "";
            this.lblLicznikPar.Font = fontSmall;
            this.lblLicznikPar.ForeColor = clrSubLabel;

            cy += 18;
            this.listPary.Location = new System.Drawing.Point(lx, cy);
            this.listPary.Size = new System.Drawing.Size(lw, 0); // wysokość dynamicznie
            this.listPary.Anchor = System.Windows.Forms.AnchorStyles.Top
                                            | System.Windows.Forms.AnchorStyles.Bottom
                                            | System.Windows.Forms.AnchorStyles.Left;
            this.listPary.FormattingEnabled = true;
            this.listPary.ItemHeight = 20;
            this.listPary.Font = fontSmall;
            this.listPary.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;

            // ── Panel lewy — dodaj wszystkie kontrolki ───────────────────────
            this.panelLewy.Controls.Add(this.btnWybierzPliki);
            this.panelLewy.Controls.Add(this.lblWybranePliki);
            this.panelLewy.Controls.Add(this.lblAdresy);
            this.panelLewy.Controls.Add(this.txtAdresy);
            this.panelLewy.Controls.Add(this.lblSeparator1);
            this.panelLewy.Controls.Add(lblParTitle);
            this.panelLewy.Controls.Add(this.lblN);
            this.panelLewy.Controls.Add(this.numN);
            this.panelLewy.Controls.Add(this.lblProg);
            this.panelLewy.Controls.Add(this.numProg);
            this.panelLewy.Controls.Add(this.lblSeparator2);
            this.panelLewy.Controls.Add(lblAdvTitle);
            this.panelLewy.Controls.Add(this.lblSloty);
            this.panelLewy.Controls.Add(this.numSloty);
            this.panelLewy.Controls.Add(this.lblTimeout);
            this.panelLewy.Controls.Add(this.numTimeout);
            this.panelLewy.Controls.Add(this.lblPort);
            this.panelLewy.Controls.Add(this.numPort);
            this.panelLewy.Controls.Add(this.lblSeparator3);
            this.panelLewy.Controls.Add(this.btnAnalyzuj);
            this.panelLewy.Controls.Add(this.btnAnuluj);
            this.panelLewy.Controls.Add(this.progressBar);
            this.panelLewy.Controls.Add(this.lblStatus);
            this.panelLewy.Controls.Add(this.lblTimer);
            this.panelLewy.Controls.Add(this.lblSeparator4);
            this.panelLewy.Controls.Add(this.lblFolder);
            this.panelLewy.Controls.Add(this.btnWczytaj);
            this.panelLewy.Controls.Add(this.lblPary);
            this.panelLewy.Controls.Add(this.lblLicznikPar);
            this.panelLewy.Controls.Add(this.listPary);

            // ================================================================
            // PANEL PLIK A
            // ================================================================
            this.panelPlikA.Location = new System.Drawing.Point(234, 0);
            this.panelPlikA.Size = new System.Drawing.Size(390, 620);
            this.panelPlikA.Anchor = System.Windows.Forms.AnchorStyles.Top
                                      | System.Windows.Forms.AnchorStyles.Bottom
                                      | System.Windows.Forms.AnchorStyles.Left
                                      | System.Windows.Forms.AnchorStyles.Right;
            this.panelPlikA.BackColor = System.Drawing.Color.White;

            this.lblNazwaA.Location = new System.Drawing.Point(4, 4);
            this.lblNazwaA.Size = new System.Drawing.Size(382, 18);
            this.lblNazwaA.Text = "Plik A";
            this.lblNazwaA.Font = fontBold;
            this.lblNazwaA.ForeColor = clrLabel;
            this.lblNazwaA.Anchor = System.Windows.Forms.AnchorStyles.Top
                                     | System.Windows.Forms.AnchorStyles.Left
                                     | System.Windows.Forms.AnchorStyles.Right;

            this.rtbPlikA.Location = new System.Drawing.Point(4, 26);
            this.rtbPlikA.Size = new System.Drawing.Size(382, 590);
            this.rtbPlikA.Anchor = System.Windows.Forms.AnchorStyles.Top
                                     | System.Windows.Forms.AnchorStyles.Bottom
                                     | System.Windows.Forms.AnchorStyles.Left
                                     | System.Windows.Forms.AnchorStyles.Right;
            this.rtbPlikA.Font = fontMono;
            this.rtbPlikA.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.rtbPlikA.ReadOnly = true;
            this.rtbPlikA.BackColor = System.Drawing.Color.White;
            this.rtbPlikA.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;

            this.panelPlikA.Controls.Add(this.lblNazwaA);
            this.panelPlikA.Controls.Add(this.rtbPlikA);

            // ================================================================
            // PANEL PLIK B
            // ================================================================
            this.panelPlikB.Location = new System.Drawing.Point(628, 0);
            this.panelPlikB.Size = new System.Drawing.Size(390, 620);
            this.panelPlikB.Anchor = System.Windows.Forms.AnchorStyles.Top
                                      | System.Windows.Forms.AnchorStyles.Bottom
                                      | System.Windows.Forms.AnchorStyles.Right;
            this.panelPlikB.BackColor = System.Drawing.Color.White;

            this.lblNazwaB.Location = new System.Drawing.Point(4, 4);
            this.lblNazwaB.Size = new System.Drawing.Size(382, 18);
            this.lblNazwaB.Text = "Plik B";
            this.lblNazwaB.Font = fontBold;
            this.lblNazwaB.ForeColor = clrLabel;
            this.lblNazwaB.Anchor = System.Windows.Forms.AnchorStyles.Top
                                     | System.Windows.Forms.AnchorStyles.Left
                                     | System.Windows.Forms.AnchorStyles.Right;

            this.rtbPlikB.Location = new System.Drawing.Point(4, 26);
            this.rtbPlikB.Size = new System.Drawing.Size(382, 590);
            this.rtbPlikB.Anchor = System.Windows.Forms.AnchorStyles.Top
                                     | System.Windows.Forms.AnchorStyles.Bottom
                                     | System.Windows.Forms.AnchorStyles.Left
                                     | System.Windows.Forms.AnchorStyles.Right;
            this.rtbPlikB.Font = fontMono;
            this.rtbPlikB.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.rtbPlikB.ReadOnly = true;
            this.rtbPlikB.BackColor = System.Drawing.Color.White;
            this.rtbPlikB.ScrollBars = System.Windows.Forms.RichTextBoxScrollBars.Vertical;

            this.panelPlikB.Controls.Add(this.lblNazwaB);
            this.panelPlikB.Controls.Add(this.rtbPlikB);

            // ================================================================
            // Form1
            // ================================================================
            ((System.ComponentModel.ISupportInitialize)(this.numN)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numProg)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numSloty)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numTimeout)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numPort)).EndInit();
            this.panelLewy.ResumeLayout(false);
            this.panelPlikA.ResumeLayout(false);
            this.panelPlikB.ResumeLayout(false);

            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1020, 620);
            this.MinimumSize = new System.Drawing.Size(800, 480);
            this.Text = "Komparator Plików";
            this.Name = "Form1";
            this.BackColor = System.Drawing.Color.FromArgb(228, 232, 240);

            this.Controls.Add(this.panelLewy);
            this.Controls.Add(this.panelPlikA);
            this.Controls.Add(this.panelPlikB);

            this.ResumeLayout(false);
            this.PerformLayout();
        }

        // ── Deklaracje pól ───────────────────────────────────────────────────
        private System.Windows.Forms.Panel panelLewy;
        private System.Windows.Forms.Panel panelPlikA;
        private System.Windows.Forms.Panel panelPlikB;

        private System.Windows.Forms.Button btnWybierzPliki;
        private System.Windows.Forms.Label lblWybranePliki;

        private System.Windows.Forms.Label lblAdresy;
        private System.Windows.Forms.TextBox txtAdresy;

        private System.Windows.Forms.Label lblSeparator1;
        private System.Windows.Forms.Label lblSeparator2;
        private System.Windows.Forms.Label lblSeparator3;
        private System.Windows.Forms.Label lblSeparator4;

        private System.Windows.Forms.Label lblN;
        private System.Windows.Forms.NumericUpDown numN;
        private System.Windows.Forms.Label lblProg;
        private System.Windows.Forms.NumericUpDown numProg;

        private System.Windows.Forms.Label lblSloty;
        private System.Windows.Forms.NumericUpDown numSloty;
        private System.Windows.Forms.Label lblTimeout;
        private System.Windows.Forms.NumericUpDown numTimeout;
        private System.Windows.Forms.Label lblPort;
        private System.Windows.Forms.NumericUpDown numPort;

        private System.Windows.Forms.Button btnAnalyzuj;
        private System.Windows.Forms.Button btnAnuluj;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblTimer;

        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.Button btnWczytaj;

        private System.Windows.Forms.Label lblPary;
        private System.Windows.Forms.Label lblLicznikPar;
        private System.Windows.Forms.ListBox listPary;

        private System.Windows.Forms.Label lblNazwaA;
        private System.Windows.Forms.RichTextBox rtbPlikA;
        private System.Windows.Forms.Label lblNazwaB;
        private System.Windows.Forms.RichTextBox rtbPlikB;
    }
}