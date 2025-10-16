using Microsoft.EntityFrameworkCore;
using PhotoApp.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();

// Lok�ln� connection string (pro localhost)
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=photoapp.db"));

// Nasazen� (deploy) - zakomentov�no
/*
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=/data/photoapp.db"));
*/

var app = builder.Build();

// AUTOMATICK� MIGRACE, aby vznikly tabulky p�i startu aplikace (puze pro produkci, zakomentov�no)

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}


if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Photos}/{action=Index}/{id?}");

app.Run();