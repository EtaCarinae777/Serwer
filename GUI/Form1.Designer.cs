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
            this.SuspendLayout();

            // listPary
            this.listPary.FormattingEnabled = true;
            this.listPary.ItemHeight = 20;
            this.listPary.Location = new System.Drawing.Point(12, 121);
            this.listPary.Name = "listPary";
            this.listPary.Size = new System.Drawing.Size(200, 600);
            this.listPary.TabIndex = 0;
            this.listPary.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Left;

            // lblPary
            this.lblPary.AutoSize = true;
            this.lblPary.Location = new System.Drawing.Point(12, 98);
            this.lblPary.Name = "lblPary";
            this.lblPary.Size = new System.Drawing.Size(70, 20);
            this.lblPary.TabIndex = 1;
            this.lblPary.Text = "Lista par";
            this.lblPary.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left;

            // rtbPlikA
            this.rtbPlikA.Location = new System.Drawing.Point(225, 41);
            this.rtbPlikA.Name = "rtbPlikA";
            this.rtbPlikA.Size = new System.Drawing.Size(550, 680);
            this.rtbPlikA.TabIndex = 2;
            this.rtbPlikA.Text = "";
            this.rtbPlikA.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Left
                | System.Windows.Forms.AnchorStyles.Right;

            // rtbPlikB
            this.rtbPlikB.Location = new System.Drawing.Point(790, 41);
            this.rtbPlikB.Name = "rtbPlikB";
            this.rtbPlikB.Size = new System.Drawing.Size(550, 680);
            this.rtbPlikB.TabIndex = 3;
            this.rtbPlikB.Text = "";
            this.rtbPlikB.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Bottom
                | System.Windows.Forms.AnchorStyles.Right;

            // lblNazwaA
            this.lblNazwaA.AutoSize = true;
            this.lblNazwaA.Location = new System.Drawing.Point(225, 18);
            this.lblNazwaA.Name = "lblNazwaA";
            this.lblNazwaA.Size = new System.Drawing.Size(48, 20);
            this.lblNazwaA.TabIndex = 4;
            this.lblNazwaA.Text = "Plik A";
            this.lblNazwaA.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left;

            // lblNazwaB
            this.lblNazwaB.AutoSize = true;
            this.lblNazwaB.Location = new System.Drawing.Point(790, 18);
            this.lblNazwaB.Name = "lblNazwaB";
            this.lblNazwaB.Size = new System.Drawing.Size(48, 20);
            this.lblNazwaB.TabIndex = 5;
            this.lblNazwaB.Text = "Plik B";
            this.lblNazwaB.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Right;

            // btnWczytaj
            this.btnWczytaj.Location = new System.Drawing.Point(12, 41);
            this.btnWczytaj.Name = "btnWczytaj";
            this.btnWczytaj.Size = new System.Drawing.Size(200, 37);
            this.btnWczytaj.TabIndex = 6;
            this.btnWczytaj.Text = "Wczytaj wyniki";
            this.btnWczytaj.UseVisualStyleBackColor = true;
            this.btnWczytaj.Click += new System.EventHandler(this.btnWczytaj_Click);
            this.btnWczytaj.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left;

            // lblFolder
            this.lblFolder.AutoSize = true;
            this.lblFolder.Location = new System.Drawing.Point(12, 18);
            this.lblFolder.Name = "lblFolder";
            this.lblFolder.Size = new System.Drawing.Size(148, 20);
            this.lblFolder.TabIndex = 7;
            this.lblFolder.Text = "Nie wybrano folderu";
            this.lblFolder.Anchor = System.Windows.Forms.AnchorStyles.Top
                | System.Windows.Forms.AnchorStyles.Left;

            // Form1
            this.AutoScaleDimensions = new System.Drawing.SizeF(9F, 20F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1400, 750);
            this.MinimumSize = new System.Drawing.Size(900, 500);
            this.Controls.Add(this.lblFolder);
            this.Controls.Add(this.btnWczytaj);
            this.Controls.Add(this.lblNazwaB);
            this.Controls.Add(this.lblNazwaA);
            this.Controls.Add(this.rtbPlikB);
            this.Controls.Add(this.rtbPlikA);
            this.Controls.Add(this.lblPary);
            this.Controls.Add(this.listPary);
            this.Name = "Form1";
            this.Text = "Komparator Plikow";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        private System.Windows.Forms.ListBox listPary;
        private System.Windows.Forms.Label lblPary;
        private System.Windows.Forms.RichTextBox rtbPlikA;
        private System.Windows.Forms.RichTextBox rtbPlikB;
        private System.Windows.Forms.Label lblNazwaA;
        private System.Windows.Forms.Label lblNazwaB;
        private System.Windows.Forms.Button btnWczytaj;
        private System.Windows.Forms.Label lblFolder;
    }
}