namespace PhotoApp.Models
{
    public class PhotoRecord
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string Code { get; set; } = "";
        public string Type { get; set; } = "";
        public string Supplier { get; set; } = "";
        public string Notes { get; set; } = "";
        public string? PhotoPath { get; set; }      // p�vodn� pole pro obr�zek (ponech pro kompatibilitu)
        public string? ImagePath { get; set; }      // nov� pole, pou��van� v controlleru
        public DateTime CreatedAt { get; set; }     // pole pro datum vytvo�en�
        public DateTime UpdatedAt { get; set; }     // pole pro datum aktualizace (voliteln�)
    }
}