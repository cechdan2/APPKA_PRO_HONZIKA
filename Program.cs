using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using PhotoApp.Data;
using PhotoApp.Models;
using PhotoApp.Services;

var builder = WebApplication.CreateBuilder(args);
ExcelPackage.License.SetNonCommercialPersonal("My Name");

// Configure SQLite connection string:
// prefer ConnectionStrings:DefaultConnection, otherwise fallback to SqliteDbPath or default file
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var dbPath = builder.Configuration["SqliteDbPath"] ?? "photoapp.db";
    // if user provided just a file name, convert to Data Source=... format
    if (!dbPath.Trim().Contains("="))
        connectionString = $"Data Source={dbPath}";
    else
        connectionString = dbPath;
}

var env = builder.Environment;
var contentRoot = env.ContentRootPath; // absolutní cesta k projektu
var dbFile = Path.Combine(contentRoot, "photoapp.db"); // canonical path used by app
var connectionStringa = $"Data Source={dbFile}";


// DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(connectionStringa));

// (volitelnì) expose the db path to configuration for other controllers
builder.Configuration["SqliteDbPath"] = dbFile;

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
builder.Services.AddScoped<IUserService, EfUserService>();

var app = builder.Build();

// AUTOMATICKÁ MIGRACE + seed uživatele
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Seed admin user pokud neexistuje
    if (!db.Users.Any())
    {
        var hasher = new PasswordHasher<CustomUser>();
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

app.Run();