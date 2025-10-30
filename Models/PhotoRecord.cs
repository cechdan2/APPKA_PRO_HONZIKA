namespace PhotoApp.Models
{
    // Reprezentace z�znamu odpov�daj�c� sloupc�m v Excelu (pro import do DB)
    public class PhotoRecord
    {
        public int Id { get; set; }

        // Excel: "Pozice" (nap�. "19 + 20")
        public string? Position { get; set; }

        // Excel: "ID" (extern� ID z Excelu)
        public string? ExternalId { get; set; }

        // Dodavatel (v Excelu sloupec "Dodavatel")
        public string? Supplier { get; set; } = "";

        // Excel: "P�vodn� n�zev" (origin�ln� n�zev / v�robce)
        public string? OriginalName { get; set; } = "";

        // U�ivatelsk�/altersn� pole Name (va�e p�vodn�)
        public string? Name { get; set; } = "";

        // K�d / intern� k�d
        public string Code { get; set; } = "";

        // Typ / kategorie
        public string? Type { get; set; } = "";

        // Excel: "material"
        public string? Material { get; set; }

        // Excel: "forma"
        public string? Form { get; set; }

        // Excel: "plnivo"
        public string? Filler { get; set; }

        // Excel: "barva"
        public string? Color { get; set; }

        // Excel: "popis"
        public string? Description { get; set; }

        // Excel: "mno�stv� m�s�c(t)" � ponech�no jako string pro flexibilitu (m��e obsahovat text jako "kusov�", "19+20" apod.)
        public string? MonthlyQuantity { get; set; }

        // Excel: "MFI" (m��e b�t ��slo nebo text, proto string)
        public string? Mfi { get; set; }

        // Pozn�mka (Excel: "Pozn�mka")
        public string? Notes { get; set; } = "";

        // Obr�zek / fotka (Excel: "Fotka") � lze ulo�it jen n�zev souboru nebo relativn� cesta
        public string? PhotoFileName { get; set; }

        // P�vodn� pole pro obr�zek (ponech pro kompatibilitu)
        public string? PhotoPath { get; set; }

        // Nov� pole, pou��van� v controlleru (relativn� cesta v wwwroot)
        public string? ImagePath { get; set; }

        // pole pro datum vytvo�en�
        public DateTime CreatedAt { get; set; }

        // pole pro datum aktualizace (voliteln�)
        public DateTime UpdatedAt { get; set; }
    }
}