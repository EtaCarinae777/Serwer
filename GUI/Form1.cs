using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace GUI
{
    public partial class Form1 : Form
    {
        private List<WynikPary> wczytaneWyniki = new List<WynikPary>();

        public Form1()
        {
            InitializeComponent();
            listPary.SelectedIndexChanged += ListPary_SelectedIndexChanged;
        }

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
                MessageBox.Show("Nie znaleziono zadnych plikow JSON w tym folderze!");
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

            // sortujemy po Jaccardzie malejaco
            wczytaneWyniki.Sort((a, b) => b.jaccard.CompareTo(a.jaccard));
            OdswiezListe();

            MessageBox.Show("Wczytano " + wczytaneWyniki.Count + " wynikow!");
        }

        private void OdswiezListe()
        {
            listPary.Items.Clear();

            foreach (var wynik in wczytaneWyniki)
            {
                string wpis = Path.GetFileName(wynik.plikA) + " vs " +
                              Path.GetFileName(wynik.plikB) + " — " +
                              wynik.jaccard.ToString("F2") + "%";
                listPary.Items.Add(wpis);
            }
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