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
        public string? PhotoPath { get; set; }      // pùvodní pole pro obrázek (ponech pro kompatibilitu)
        public string? ImagePath { get; set; }      // nové pole, používané v controlleru
        public DateTime CreatedAt { get; set; }     // pole pro datum vytvoøení
        public DateTime UpdatedAt { get; set; }     // pole pro datum aktualizace (volitelnì)
    }
}