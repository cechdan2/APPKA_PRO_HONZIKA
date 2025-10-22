using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using PhotoApp.Data;
using PhotoApp.Services;
using PhotoApp.Models;

var builder = WebApplication.CreateBuilder(args);

// DbContext
builder.Services.AddDbContext<AppDbContext>
    (options =>
    options.UseSqlite("Data Source=photoapp.db"));

// Cookie authentication (vlastní)
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
.AddCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.LogoutPath = "/Account/Logout";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(8);
});

builder.Services.AddAuthorization();

builder.Services.AddHttpContextAccessor();
builder.Services.AddControllersWithViews();

// registrace vlastního user servisu
builder.Services.AddScoped<IUserService, EfUserService>
    ();

var app = builder.Build();

// AUTOMATICKÁ MIGRACE + seed uživatele
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>
        ();
    db.Database.Migrate();

    // Seed admin user pokud neexistuje
    if (!db.Users.Any())
    {
        var hasher = new PasswordHasher<CustomUser>
            ();
        var admin = new CustomUser { UserName = "admin" };
        admin.PasswordHash = hasher.HashPassword(admin, "P@ssw0rd!");
        db.Users.Add(admin);
        db.SaveChanges();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
name: "default",
pattern: "{controller=Account}/{action=Login}/{id?}");

// Pozn.: MapRazorPages() odstranìno — Identity UI už nepoužíváme.

app.Run();
