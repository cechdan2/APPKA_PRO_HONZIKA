using System.ComponentModel.DataAnnotations;

namespace PhotoApp.Models
{
    public class CustomUser
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [Required]
        public string UserName { get; set; } = string.Empty;

        // Ukládáme pouze hash hesla
        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        // Přidat další pole (Email, Role atd.) podle potřeby
    }
}