using PhotoApp.Models;

namespace PhotoApp.Services
{
    public interface IUserService
    {
        CustomUser? FindByName(string userName);
        bool ValidateCredentials(string userName, string password, out CustomUser? user);
        // Pokud chcete administraci uživatelů, přidejte metody Create, Update, Delete (neveřejné UI).
    }
}