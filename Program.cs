using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using PhotoApp.Data;
using PhotoApp.Services;
using PhotoApp.Models;
// pøidejte nahoøe souboru
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using OfficeOpenXml;

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


SetEPPlusLicense();

static void SetEPPlusLicense()
{
    try
    {
        var excelPackageType = typeof(ExcelPackage);

        // 1) Pokus o novìjší property "License"
        var licenseProp = excelPackageType.GetProperty("License", BindingFlags.Public | BindingFlags.Static);
        if (licenseProp != null)
        {
            // získat instanci (èasto nebývá null)
            var licenseObj = licenseProp.GetValue(null);
            if (licenseObj != null)
            {
                // zkusíme zavolat metody které mohou nastavit kontext (SetLicense / SetLicenseContext / SetContext)
                var tryMethodNames = new[] { "SetLicense", "SetLicenseContext", "SetContext", "Set" };
                foreach (var name in tryMethodNames)
                {
                    var m = licenseObj.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(OfficeOpenXml.LicenseContext) }, null);
                    if (m != null)
                    {
                        m.Invoke(licenseObj, new object[] { OfficeOpenXml.LicenseContext.NonCommercial });
                        return;
                    }
                }

                // zkusit nastavit property "Context" pokud existuje
                var ctxProp = licenseObj.GetType().GetProperty("Context", BindingFlags.Public | BindingFlags.Instance);
                if (ctxProp != null && ctxProp.CanWrite && ctxProp.PropertyType == typeof(OfficeOpenXml.LicenseContext))
                {
                    ctxProp.SetValue(licenseObj, OfficeOpenXml.LicenseContext.NonCommercial);
                    return;
                }

                // fallback: najít jakoukoli metodu s jedním enum parametrem a zavolat ji
                var methods = licenseObj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                foreach (var m in methods)
                {
                    var ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType.IsEnum)
                    {
                        m.Invoke(licenseObj, new object[] { OfficeOpenXml.LicenseContext.NonCommercial });
                        return;
                    }
                }
            }
            else
            {
                // licenseProp existuje ale vrací null - pokud lze pøiøadit pøímo, zkusíme to (ménì èasté)
                if (licenseProp.CanWrite && licenseProp.PropertyType == typeof(OfficeOpenXml.LicenseContext))
                {
                    licenseProp.SetValue(null, OfficeOpenXml.LicenseContext.NonCommercial);
                    return;
                }
            }
        }

        // 2) Fallback na starší API: static LicenseContext property (nìkteré verze EPPlus)
        var licenseContextProp = excelPackageType.GetProperty("LicenseContext", BindingFlags.Public | BindingFlags.Static);
        if (licenseContextProp != null && licenseContextProp.CanWrite)
        {
            licenseContextProp.SetValue(null, OfficeOpenXml.LicenseContext.NonCommercial);
            return;
        }

        // pokud jsme nedokázali nastavit licenci, dáme varování (nepøerušujeme start, ale import mùže pozdìji selhat)
        Console.WriteLine("Warning: EPPlus license property not found or not writable. If import fails, adjust EPPlus license configuration.");
    }
    catch (Exception ex)
    {
        // log a pokraèuj
        Console.WriteLine("Warning: Failed to set EPPlus license automatically: " + ex.Message);
    }
}



app.Run();
