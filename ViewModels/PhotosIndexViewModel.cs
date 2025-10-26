using System.Collections.Generic;
using PhotoApp.Models;

namespace PhotoApp.ViewModels
{
    public class PhotosIndexViewModel
    {
        // výsledné položky pro zobrazení
        public List<PhotoRecord> Items { get; set; } = new List<PhotoRecord>();

        // dostupné hodnoty do selectů
        public List<string> Suppliers { get; set; } = new List<string>();
        public List<string> Materials { get; set; } = new List<string>();
        public List<string> Types { get; set; } = new List<string>();
        public List<string> Colors { get; set; } = new List<string>();

        // nově: jména / pozice / plniva
        public List<string> Names { get; set; } = new List<string>();
        public List<string> Positions { get; set; } = new List<string>();
        public List<string> Fillers { get; set; } = new List<string>();

        // aktuální filtry (vázané na query string)
        public string Search { get; set; }
        public string Supplier { get; set; }
        public string Material { get; set; }
        public string Type { get; set; }
        public string Color { get; set; }

        // nově: aktivní hodnoty
        public string Name { get; set; }
        public string Position { get; set; }
        public string Filler { get; set; }
    }
}