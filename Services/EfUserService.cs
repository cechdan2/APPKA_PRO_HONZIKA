using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhotoApp.Data;
using PhotoApp.Models;

namespace PhotoApp.Services
{
    public class EfUserService : IUserService
    {
        private readonly AppDbContext _db;
        private readonly PasswordHasher<CustomUser> _hasher = new();

        public EfUserService(AppDbContext db)
        {
            _db = db;
        }

        public CustomUser? FindByName(string userName)
            => _db.Users.AsNoTracking().FirstOrDefault(u => u.UserName.ToLower() == userName.ToLower());

        public bool ValidateCredentials(string userName, string password, out CustomUser? user)
        {
            user = _db.Users.FirstOrDefault(u => u.UserName.ToLower() == userName.ToLower());
            if (user == null) return false;

            var res = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
            return res == PasswordVerificationResult.Success;
        }
    }
}