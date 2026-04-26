namespace GUI
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            this.listPary = new System.Windows.Forms.ListBox();
            this.lblPary = new System.Windows.Forms.Label();
            this.rtbPlikA = new System.Windows.Forms.RichTextBox();
            this.rtbPlikB = new System.Windows.Forms.RichTextBox();
            this.lblNazwaA = new System.Windows.Forms.Label();
            this.lblNazwaB = new System.Windows.Forms.Label();
            this.btnWczytaj = new System.Windows.Forms.Button();
            this.lblFolder = new System.Windows.Forms.Label();
            this.lblTimer = new System.Windows.Forms.Label();
            this.btnWybierzPliki = new System.Windows.Forms.Button();
            this.txtAdresy = new System.Windows.Forms.TextBox();
            this.lblAdresy = new System.Windows.Forms.Label();
            this.numN = new System.Windows.Forms.NumericUpDown();
            this.lblN = new System.Windows.Forms.Label();
            this.numGrainSize = new System.Windows.Forms.NumericUpDown();
            this.lblGrainSize = new System.Windows.Forms.Label();
            this.numProg = new System.Windows.Forms.NumericUpDown();
            this.lblProg = new System.Windows.Forms.Label();
            this.btnAnalyzuj = new System.Windows.Forms.Button();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblWybranePliki = new System.Windows.Forms.Label();
            this.panelLewy = new System.Windows.Forms.Panel();

            ((System.ComponentModel.ISupportInitialize)(this.numN)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numGrainSize)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numProg)).BeginInit();
            this.panelLewy.SuspendLayout();
            this.SuspendLayout();

            // ---------------------------------------------------------------
            // panelLewy — kontener lewej kolumny (szerokość 210px)
            // ---------------------------------------------------------------
            this.panelLewy.Location = new System.Drawing.Point(4, 4);
            this.panelLewy.Size = new System.Drawing.Size(210, 580);
            this.panelLewy.Anchor = ((System.Windows.Forms.AnchorStyles)(
                System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left));
            this.panelLewy.Name = "panelLewy";

            // ---------------------------------------------------------------
            //stoper
            // ---------------------------------------------------------------
            this.lblTimer.Location = new System.Drawing.Point(4, 374); // Pozycja pod statusem
            this.lblTimer.Size = new System.Drawing.Size(200, 16);
            this.lblTimer.Name = "lblTimer";
            this.lblTimer.Text = "Czas: 00:00:00";
            this.lblTimer.Font = new System.Drawing.Font("Segoe UI", 8f, System.Drawing.FontStyle.Bold);

            // ---------------------------------------------------------------
            // btnWybierzPliki
            // ---------------------------------------------------------------
            this.btnWybierzPliki.Location = new System.Drawing.Point(4, 4);
            this.btnWybierzPliki.Size = new System.Drawing.Size(200, 26);
            this.btnWybierzPliki.Name = "btnWybierzPliki";
            this.btnWybierzPliki.Text = "Wybierz pliki do analizy...";
            this.btnWybierzPliki.UseVisualStyleBackColor = true;
            this.btnWybierzPliki.Click += new System.EventHandler(this.btnWybierzPliki_Click);

            // ---------------------------------------------------------------
            // lblWybranePliki — pokazuje ile plików wybrano
            // ---------------------------------------------------------------
            this.lblWybranePliki.Location = new System.Drawing.Point(4, 34);
            this.lblWybranePliki.Size = new System.Drawing.Size(200, 16);
            this.lblWybranePliki.Name = "lblWybranePliki";
            this.lblWybranePliki.Text = "Nie wybrano plików";
            this.lblWybranePliki.Font = new System.Drawing.Font("Segoe UI", 8f, System.Drawing.FontStyle.Italic);
            this.lblWybranePliki.ForeColor = System.Drawing.Color.Gray;

            // ---------------------------------------------------------------
            // lblAdresy
            // ---------------------------------------------------------------
            this.lblAdresy.Location = new System.Drawing.Point(4, 58);
            this.lblAdresy.Size = new System.Drawing.Size(200, 16);
            this.lblAdresy.Name = "lblAdresy";
            this.lblAdresy.Text = "Adresy serwerów (jeden na linię):";

            // ---------------------------------------------------------------
            // txtAdresy — MultiLine, ScrollBars, jeden adres na linię
            // ---------------------------------------------------------------
            this.txtAdresy.Location = new System.Drawing.Point(4, 76);
            this.txtAdresy.Size = new System.Drawing.Size(200, 60);
            this.txtAdresy.Name = "txtAdresy";
            this.txtAdresy.Multiline = true;
            this.txtAdresy.ScrollBars = System.Windows.Forms.ScrollBars.Vertical;
            this.txtAdresy.Text = "127.0.0.1";

            // ---------------------------------------------------------------
            // lblN + numN
            // ---------------------------------------------------------------
            this.lblN.Location = new System.Drawing.Point(4, 144);
            this.lblN.Size = new System.Drawing.Size(100, 16);
            this.lblN.Name = "lblN";
            this.lblN.Text = "n (rozmiar n-gramów):";

            this.numN.Location = new System.Drawing.Point(4, 162);
            this.numN.Size = new System.Drawing.Size(80, 22);
            this.numN.Name = "numN";
            this.numN.Minimum = 1;
            this.numN.Maximum = 20;
            this.numN.Value = 4;

            // ---------------------------------------------------------------
            // lblGrainSize + numGrainSize
            // ---------------------------------------------------------------
            this.lblGrainSize.Location = new System.Drawing.Point(4, 190);
            this.lblGrainSize.Size = new System.Drawing.Size(120, 16);
            this.lblGrainSize.Name = "lblGrainSize";
            this.lblGrainSize.Text = "grainSize:";

            this.numGrainSize.Location = new System.Drawing.Point(4, 208);
            this.numGrainSize.Size = new System.Drawing.Size(80, 22);
            this.numGrainSize.Name = "numGrainSize";
            this.numGrainSize.Minimum = 1;
            this.numGrainSize.Maximum = 100;
            this.numGrainSize.Value = 1;

            // ---------------------------------------------------------------
            // lblProg + numProg
            // ---------------------------------------------------------------
            this.lblProg.Location = new System.Drawing.Point(4, 236);
            this.lblProg.Size = new System.Drawing.Size(150, 16);
            this.lblProg.Name = "lblProg";
            this.lblProg.Text = "Próg plagiatu (%):";

            this.numProg.Location = new System.Drawing.Point(4, 254);
            this.numProg.Size = new System.Drawing.Size(80, 22);
            this.numProg.Name = "numProg";
            this.numProg.Minimum = 0;
            this.numProg.Maximum = 100;
            this.numProg.DecimalPlaces = 1;
            this.numProg.Increment = new decimal(new int[] { 5, 0, 0, 0 });
            this.numProg.Value = new decimal(new int[] { 300, 0, 0, 65536 }); // 30.0

            // ---------------------------------------------------------------
            // btnAnalyzuj
            // ---------------------------------------------------------------
            this.btnAnalyzuj.Location = new System.Drawing.Point(4, 284);
            this.btnAnalyzuj.Size = new System.Drawing.Size(200, 30);
            this.btnAnalyzuj.Name = "btnAnalyzuj";
            this.btnAnalyzuj.Text = "▶  Analizuj";
            this.btnAnalyzuj.UseVisualStyleBackColor = true;
            this.btnAnalyzuj.Font = new System.Drawing.Font("Segoe UI", 9f, System.Drawing.FontStyle.Bold);
            this.btnAnalyzuj.BackColor = System.Drawing.Color.FromArgb(0, 120, 215);
            this.btnAnalyzuj.ForeColor = System.Drawing.Color.White;
            this.btnAnalyzuj.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnAnalyzuj.Click += new System.EventHandler(this.btnAnalyzuj_Click);

            // ---------------------------------------------------------------
            // progressBar
            // ---------------------------------------------------------------
            this.progressBar.Location = new System.Drawing.Point(4, 322);
            this.progressBar.Size = new System.Drawing.Size(200, 16);
            this.progressBar.Name = "progressBar";
            this.progressBar.Minimum = 0;
            this.progressBar.Value = 0;
            this.progressBar.Style = System.Windows.Forms.ProgressBarStyle.Continuous;

            // ---------------------------------------------------------------
            // lblStatus
            // ---------------------------------------------------------------
            this.lblStatus.Location = new System.Drawing.Point(4, 342);
            this.lblStatus.Size = new System.Drawing.Size(200, 32);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Text = "";
            this.lblStatus.Font = new System.Drawing.Font("Segoe UI", 8f);
            this.lblStatus.ForeColor = System.Drawing.Color.DimGray;

            // ---------------------------------------------------------------
            // Separator — istniejące kontrolki wyników poniżej
            // ---------------------------------------------------------------

            // lblFolder — teraz tytuł sekcji wczytywania z pliku
            this.lblFolder.Location = new System.Drawing.Point(4, 386);
            this.lblFolder.Size = new System.Drawing.Size(200, 16);
            this.lblFolder.Name = "lblFolder";
            this.lblFolder.Text = "Nie wybrano folderu";
            this.lblFolder.Font = new System.Drawing.Font("Segoe UI", 8f, System.Drawing.FontStyle.Italic);
            this.lblFolder.ForeColor = System.Drawing.Color.Gray;

            // btnWczytaj — wczytywanie zapisanych JSON-ów
            this.btnWczytaj.Location = new System.Drawing.Point(4, 406);
            this.btnWczytaj.Size = new System.Drawing.Size(200, 26);
            this.btnWczytaj.Name = "btnWczytaj";
            this.btnWczytaj.Text = "Wczytaj wyniki z folderu...";
            this.btnWczytaj.UseVisualStyleBackColor = true;
            this.btnWczytaj.Click += new System.EventHandler(this.btnWczytaj_Click);

            // lblPary
            this.lblPary.Location = new System.Drawing.Point(4, 440);
            this.lblPary.Size = new System.Drawing.Size(200, 16);
            this.lblPary.Name = "lblPary";
            this.lblPary.Text = "Lista par:";

            // listPary
            this.listPary.Location = new System.Drawing.Point(4, 458);
            this.listPary.Size = new System.Drawing.Size(200, 112);
            this.listPary.Anchor = ((System.Windows.Forms.AnchorStyles)(
                System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left));
            this.listPary.Name = "listPary";
            this.listPary.FormattingEnabled = true;

            // ---------------------------------------------------------------
            // Panel lewy — dodaj wszystkie kontrolki lewej kolumny
            // ---------------------------------------------------------------
            this.panelLewy.Controls.Add(this.btnWybierzPliki);
            this.panelLewy.Controls.Add(this.lblWybranePliki);
            this.panelLewy.Controls.Add(this.lblAdresy);
            this.panelLewy.Controls.Add(this.txtAdresy);
            this.panelLewy.Controls.Add(this.lblN);
            this.panelLewy.Controls.Add(this.numN);
            this.panelLewy.Controls.Add(this.lblGrainSize);
            this.panelLewy.Controls.Add(this.numGrainSize);
            this.panelLewy.Controls.Add(this.lblProg);
            this.panelLewy.Controls.Add(this.numProg);
            this.panelLewy.Controls.Add(this.btnAnalyzuj);
            this.panelLewy.Controls.Add(this.progressBar);
            this.panelLewy.Controls.Add(this.lblStatus);
            this.panelLewy.Controls.Add(this.lblFolder);
            this.panelLewy.Controls.Add(this.btnWczytaj);
            this.panelLewy.Controls.Add(this.lblPary);
            this.panelLewy.Controls.Add(this.listPary);
            this.panelLewy.Controls.Add(this.lblTimer);

            //----------------------------------------------------------------
            //stoper
            //----------------------------------------------------------------




            // ---------------------------------------------------------------
            // lblNazwaA
            // ---------------------------------------------------------------
            this.lblNazwaA.AutoSize = true;
            this.lblNazwaA.Location = new System.Drawing.Point(220, 8);
            this.lblNazwaA.Name = "lblNazwaA";
            this.lblNazwaA.Size = new System.Drawing.Size(34, 13);
            this.lblNazwaA.Text = "Plik A";

            // ---------------------------------------------------------------
            // rtbPlikA
            // ---------------------------------------------------------------
            this.rtbPlikA.Location = new System.Drawing.Point(220, 26);
            this.rtbPlikA.Size = new System.Drawing.Size(380, 548);
            this.rtbPlikA.Anchor = ((System.Windows.Forms.AnchorStyles)(
                System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Left |
                System.Windows.Forms.AnchorStyles.Right));
            this.rtbPlikA.Name = "rtbPlikA";
            this.rtbPlikA.Text = "";

            // ---------------------------------------------------------------
            // lblNazwaB
            // ---------------------------------------------------------------
            this.lblNazwaB.AutoSize = true;
            this.lblNazwaB.Anchor = ((System.Windows.Forms.AnchorStyles)(
                System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Right));
            this.lblNazwaB.Location = new System.Drawing.Point(610, 8);
            this.lblNazwaB.Name = "lblNazwaB";
            this.lblNazwaB.Size = new System.Drawing.Size(34, 13);
            this.lblNazwaB.Text = "Plik B";

            // ---------------------------------------------------------------
            // rtbPlikB
            // ---------------------------------------------------------------
            this.rtbPlikB.Location = new System.Drawing.Point(610, 26);
            this.rtbPlikB.Size = new System.Drawing.Size(360, 548);
            this.rtbPlikB.Anchor = ((System.Windows.Forms.AnchorStyles)(
                System.Windows.Forms.AnchorStyles.Top |
                System.Windows.Forms.AnchorStyles.Bottom |
                System.Windows.Forms.AnchorStyles.Right));
            this.rtbPlikB.Name = "rtbPlikB";
            this.rtbPlikB.Text = "";

            // ---------------------------------------------------------------
            // Form1
            // ---------------------------------------------------------------
            ((System.ComponentModel.ISupportInitialize)(this.numN)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numGrainSize)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numProg)).EndInit();
            this.panelLewy.ResumeLayout(false);

            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(980, 580);
            this.MinimumSize = new System.Drawing.Size(720, 420);
            this.Text = "Komparator Plików";
            this.Name = "Form1";

            this.Controls.Add(this.panelLewy);
            this.Controls.Add(this.lblNazwaA);
            this.Controls.Add(this.rtbPlikA);
            this.Controls.Add(this.lblNazwaB);
            this.Controls.Add(this.rtbPlikB);

            this.ResumeLayout(false);
            this.PerformLayout();

            //---------------------------------------------------------------
            //Timer
            // ---------------------------------------------------------------


        }

        private System.Windows.Forms.ListBox listPary;
        private System.Windows.Forms.Label lblPary;
        private System.Windows.Forms.RichTextBox rtbPlikA;
        private System.Windows.Forms.RichTextBox rtbPlikB;
        private System.Windows.Forms.Label lblNazwaA;
        private System.Windows.Forms.Label lblNazwaB;
        private System.Windows.Forms.Button btnWczytaj;
        private System.Windows.Forms.Label lblFolder;
        private System.Windows.Forms.Button btnWybierzPliki;
        private System.Windows.Forms.Label lblWybranePliki;
        private System.Windows.Forms.TextBox txtAdresy;
        private System.Windows.Forms.Label lblAdresy;
        private System.Windows.Forms.NumericUpDown numN;
        private System.Windows.Forms.Label lblN;
        private System.Windows.Forms.NumericUpDown numGrainSize;
        private System.Windows.Forms.Label lblGrainSize;
        private System.Windows.Forms.NumericUpDown numProg;
        private System.Windows.Forms.Label lblProg;
        private System.Windows.Forms.Button btnAnalyzuj;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Panel panelLewy;
        private System.Windows.Forms.Label lblTimer;
    }
}