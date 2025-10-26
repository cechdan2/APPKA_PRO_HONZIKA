using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using PhotoApp.Data;
using PhotoApp.Models;
using System.Text;
using System.Text.Json;

namespace PhotoApp.Controllers;

[Authorize]
public class PhotosController : Controller
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB
    private static readonly string[] PermittedTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };

    public PhotosController(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }

    public IActionResult Import()
    {
        return View();
    }

    [Authorize]
    // vlož tento using do horní části souboru:
    // using PhotoApp.ViewModels;
    // doplňte required using: using PhotoApp.ViewModels;
    public async Task<IActionResult> Index(string search, string supplier, string material, string type, string color, string name, string position, string filler, bool forceDesktop = false)
    {
        // Detekce mobilního zařízení na základě User-Agent
        var userAgent = HttpContext.Request.Headers["User-Agent"].ToString();
        bool isMobile = userAgent.Contains("Mobile") || userAgent.Contains("Android") || userAgent.Contains("iPhone") || userAgent.Contains("iPad") || userAgent.Contains("Windows Phone");

        if (isMobile && !forceDesktop)
        {
            return RedirectToAction("Index_phone", new { search, supplier, material, type, color, name, position, filler, forceDesktop });
        }

        // připrav query
        var q = _context.Photos.AsNoTracking().AsQueryable();

        // fulltext-like vyhledávání přes několik polí (pokryje i Name)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(p =>
                EF.Functions.Like(p.Name, $"%{s}%") ||
                EF.Functions.Like(p.OriginalName, $"%{s}%") ||
                EF.Functions.Like(p.Description, $"%{s}%") ||
                EF.Functions.Like(p.Notes, $"%{s}%") ||
                EF.Functions.Like(p.Code, $"%{s}%")
            );
        }

        if (!string.IsNullOrWhiteSpace(supplier))
            q = q.Where(p => p.Supplier == supplier);

        if (!string.IsNullOrWhiteSpace(material))
            q = q.Where(p => p.Material == material);

        if (!string.IsNullOrWhiteSpace(type))
            q = q.Where(p => p.Type == type);

        if (!string.IsNullOrWhiteSpace(color))
            q = q.Where(p => p.Color == color);

        // nové přesné filtry
        if (!string.IsNullOrWhiteSpace(name))
            q = q.Where(p => p.Name == name);

        if (!string.IsNullOrWhiteSpace(position))
            q = q.Where(p => p.Position == position);

        if (!string.IsNullOrWhiteSpace(filler))
            q = q.Where(p => p.Filler == filler);

        // načti položky (třídění podle potřeby)
        var items = await q.OrderByDescending(p => p.UpdatedAt).ToListAsync();

        // naplň seznamy pro selecty (distinct hodnoty)
        var suppliers = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Supplier))
            .Select(p => p.Supplier)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var materials = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Material))
            .Select(p => p.Material)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var types = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Type))
            .Select(p => p.Type)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var colors = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Color))
            .Select(p => p.Color)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        // nové seznamy
        var names = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .Select(p => p.Name)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var positions = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Position))
            .Select(p => p.Position)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var fillers = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Filler))
            .Select(p => p.Filler)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var vm = new PhotoApp.ViewModels.PhotosIndexViewModel
        {
            Items = items,
            Suppliers = suppliers,
            Materials = materials,
            Types = types,
            Colors = colors,
            Names = names,
            Positions = positions,
            Fillers = fillers,
            Search = search,
            Supplier = supplier,
            Material = material,
            Type = type,
            Color = color,
            Name = name,
            Position = position,
            Filler = filler
        };

        return View(vm);
    }

    // Nová akce pro mobilní zařízení
    [Authorize]
    public async Task<IActionResult> Index_phone(string search, string supplier, string material, string type, string color, string name, string position, string filler, bool forceDesktop = false)
    {
        // Zde můžete implementovat logiku podobnou Index, ale s úpravami pro mobil (např. jiné view nebo data)
        // Pro jednoduchost, můžete použít stejnou logiku jako v Index, ale vrátit jiné View
        // Příklad: return View("Index_phone", vm); kde Index_phone.cshtml je upravený view pro mobil

        // Pro účely této odpovědi předpokládám, že budete mít Index_phone.cshtml s mobilní logikou
        // Zde je stejná logika jako v Index pro demonstraci

        if (forceDesktop)
        {
            // Přepnout na desktop view
            // připrav query
            var q = _context.Photos.AsNoTracking().AsQueryable();

            // fulltext-like vyhledávání přes několik polí (pokryje i Name)
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(p =>
                    EF.Functions.Like(p.Name, $"%{s}%") ||
                    EF.Functions.Like(p.OriginalName, $"%{s}%") ||
                    EF.Functions.Like(p.Description, $"%{s}%") ||
                    EF.Functions.Like(p.Notes, $"%{s}%") ||
                    EF.Functions.Like(p.Code, $"%{s}%")
                );
            }

            if (!string.IsNullOrWhiteSpace(supplier))
                q = q.Where(p => p.Supplier == supplier);

            if (!string.IsNullOrWhiteSpace(material))
                q = q.Where(p => p.Material == material);

            if (!string.IsNullOrWhiteSpace(type))
                q = q.Where(p => p.Type == type);

            if (!string.IsNullOrWhiteSpace(color))
                q = q.Where(p => p.Color == color);

            // nové přesné filtry
            if (!string.IsNullOrWhiteSpace(name))
                q = q.Where(p => p.Name == name);

            if (!string.IsNullOrWhiteSpace(position))
                q = q.Where(p => p.Position == position);

            if (!string.IsNullOrWhiteSpace(filler))
                q = q.Where(p => p.Filler == filler);

            // načti položky (třídění podle potřeby)
            var items = await q.OrderByDescending(p => p.UpdatedAt).ToListAsync();

            // naplň seznamy pro selecty (distinct hodnoty)
            var suppliers = await _context.Photos
                .Where(p => !string.IsNullOrEmpty(p.Supplier))
                .Select(p => p.Supplier)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var materials = await _context.Photos
                .Where(p => !string.IsNullOrEmpty(p.Material))
                .Select(p => p.Material)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var types = await _context.Photos
                .Where(p => !string.IsNullOrEmpty(p.Type))
                .Select(p => p.Type)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var colors = await _context.Photos
                .Where(p => !string.IsNullOrEmpty(p.Color))
                .Select(p => p.Color)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            // nové seznamy
            var names = await _context.Photos
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .Select(p => p.Name)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var positions = await _context.Photos
                .Where(p => !string.IsNullOrEmpty(p.Position))
                .Select(p => p.Position)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var fillers = await _context.Photos
                .Where(p => !string.IsNullOrEmpty(p.Filler))
                .Select(p => p.Filler)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();

            var vm = new PhotoApp.ViewModels.PhotosIndexViewModel
            {
                Items = items,
                Suppliers = suppliers,
                Materials = materials,
                Types = types,
                Colors = colors,
                Names = names,
                Positions = positions,
                Fillers = fillers,
                Search = search,
                Supplier = supplier,
                Material = material,
                Type = type,
                Color = color,
                Name = name,
                Position = position,
                Filler = filler
            };

            return View(vm); // Vrátí Index.cshtml (desktop)
        }

        // připrav query
        var q2 = _context.Photos.AsNoTracking().AsQueryable();

        // fulltext-like vyhledávání přes několik polí (pokryje i Name)
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q2 = q2.Where(p =>
                EF.Functions.Like(p.Name, $"%{s}%") ||
                EF.Functions.Like(p.OriginalName, $"%{s}%") ||
                EF.Functions.Like(p.Description, $"%{s}%") ||
                EF.Functions.Like(p.Notes, $"%{s}%") ||
                EF.Functions.Like(p.Code, $"%{s}%")
            );
        }

        if (!string.IsNullOrWhiteSpace(supplier))
            q2 = q2.Where(p => p.Supplier == supplier);

        if (!string.IsNullOrWhiteSpace(material))
            q2 = q2.Where(p => p.Material == material);

        if (!string.IsNullOrWhiteSpace(type))
            q2 = q2.Where(p => p.Type == type);

        if (!string.IsNullOrWhiteSpace(color))
            q2 = q2.Where(p => p.Color == color);

        // nové přesné filtry
        if (!string.IsNullOrWhiteSpace(name))
            q2 = q2.Where(p => p.Name == name);

        if (!string.IsNullOrWhiteSpace(position))
            q2 = q2.Where(p => p.Position == position);

        if (!string.IsNullOrWhiteSpace(filler))
            q2 = q2.Where(p => p.Filler == filler);

        // načti položky (třídění podle potřeby)
        var items2 = await q2.OrderByDescending(p => p.UpdatedAt).ToListAsync();

        // naplň seznamy pro selecty (distinct hodnoty)
        var suppliers2 = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Supplier))
            .Select(p => p.Supplier)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var materials2 = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Material))
            .Select(p => p.Material)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var types2 = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Type))
            .Select(p => p.Type)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var colors2 = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Color))
            .Select(p => p.Color)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        // nové seznamy
        var names2 = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .Select(p => p.Name)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var positions2 = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Position))
            .Select(p => p.Position)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var fillers2 = await _context.Photos
            .Where(p => !string.IsNullOrEmpty(p.Filler))
            .Select(p => p.Filler)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        var vm2 = new PhotoApp.ViewModels.PhotosIndexViewModel
        {
            Items = items2,
            Suppliers = suppliers2,
            Materials = materials2,
            Types = types2,
            Colors = colors2,
            Names = names2,
            Positions = positions2,
            Fillers = fillers2,
            Search = search,
            Supplier = supplier,
            Material = material,
            Type = type,
            Color = color,
            Name = name,
            Position = position,
            Filler = filler
        };

        return View("Index_phone", vm2); // Vrátí Index_phone.cshtml
    }

    // GET: Photos/Create
    public IActionResult Create() => View();

    // POST: Photos/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PhotoRecord photoModel, IFormFile? PhotoFile)
    {
        // Pozn.: photoModel je to, co se vázalo z formu. Nechceme používat Bind(...) takto široce.
        if (!ModelState.IsValid)
            return View(photoModel);

        // zpracuj nahrání souboru (pokud je)
        string? savedPath = null;
        if (PhotoFile != null && PhotoFile.Length > 0)
        {
            if (PhotoFile.Length > MaxFileSize)
            {
                ModelState.AddModelError("PhotoFile", "Soubor je příliš velký.");
                return View(photoModel);
            }

            if (!PermittedTypes.Contains(PhotoFile.ContentType))
            {
                ModelState.AddModelError("PhotoFile", "Nepodporovaný typ souboru.");
                return View(photoModel);
            }

            var uploads = Path.Combine(_env.WebRootPath, "uploads");
            if (!Directory.Exists(uploads))
                Directory.CreateDirectory(uploads);

            var fileName = Guid.NewGuid() + Path.GetExtension(PhotoFile.FileName);
            var path = Path.Combine(uploads, fileName);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await PhotoFile.CopyToAsync(stream);
            }

            savedPath = "/uploads/" + fileName;
        }

        // Explicitní mapování — vytvoříme novou entitu a přiřadíme všechna relevantní pole
        var photo = new PhotoRecord
        {
            // Zkopíruj všechna pole, která máš ve view
            Position = photoModel.Position,
            ExternalId = photoModel.ExternalId,
            OriginalName = photoModel.OriginalName,
            Material = photoModel.Material,
            Form = photoModel.Form,
            Filler = photoModel.Filler,
            Color = photoModel.Color,
            Mfi = photoModel.Mfi,
            MonthlyQuantity = photoModel.MonthlyQuantity,
            Name = photoModel.Name,
            Code = photoModel.Code,
            Type = photoModel.Type,
            Supplier = photoModel.Supplier,
            Description = photoModel.Description,
            Notes = photoModel.Notes,
            // cesta k fotce z uploadu (pokud byla)
            PhotoPath = savedPath ?? photoModel.PhotoPath,
            ImagePath = photoModel.ImagePath, // pokud používáš ImagePath
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Add(photo);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // GET: Photos/Edit/5
    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();

        var photo = await _context.Photos.FindAsync(id);
        if (photo == null)
            return NotFound();

        return View(photo);
    }

    // POST: Photos/Edit/5
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, PhotoRecord photoModel, IFormFile? PhotoFile)
    {
        if (id != photoModel.Id)
            return NotFound();

        if (!ModelState.IsValid)
            return View(photoModel);

        try
        {
            // Načteme existující entitu z DB a explicitně ji aktualizujeme (bez overpostingu)
            var existing = await _context.Photos.FirstOrDefaultAsync(p => p.Id == id);
            if (existing == null) return NotFound();

            // zpracování nového souboru (pokud existuje)
            if (PhotoFile != null && PhotoFile.Length > 0)
            {
                if (PhotoFile.Length > MaxFileSize)
                {
                    ModelState.AddModelError("PhotoFile", "Soubor je příliš velký.");
                    return View(photoModel);
                }

                if (!PermittedTypes.Contains(PhotoFile.ContentType))
                {
                    ModelState.AddModelError("PhotoFile", "Nepodporovaný typ souboru.");
                    return View(photoModel);
                }

                var uploads = Path.Combine(_env.WebRootPath, "uploads");
                if (!Directory.Exists(uploads))
                    Directory.CreateDirectory(uploads);

                var fileName = Guid.NewGuid() + Path.GetExtension(PhotoFile.FileName);
                var path = Path.Combine(uploads, fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await PhotoFile.CopyToAsync(stream);
                }

                // smazání starého souboru pokud existuje
                if (!string.IsNullOrEmpty(existing.PhotoPath))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, existing.PhotoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(oldPath))
                    {
                        System.IO.File.Delete(oldPath);
                    }
                }

                existing.PhotoPath = "/uploads/" + fileName;
            }
            // jinak necháme existing.PhotoPath beze změny (nerozepisujeme z modelu)

            // Explicitně přiřadíme vlastnosti z modelu
            existing.Position = photoModel.Position;
            existing.ExternalId = photoModel.ExternalId;
            existing.OriginalName = photoModel.OriginalName;
            existing.Material = photoModel.Material;
            existing.Form = photoModel.Form;
            existing.Filler = photoModel.Filler;
            existing.Color = photoModel.Color;
            existing.Mfi = photoModel.Mfi;
            existing.MonthlyQuantity = photoModel.MonthlyQuantity;
            existing.Name = photoModel.Name;
            existing.Code = photoModel.Code;
            existing.Type = photoModel.Type;
            existing.Supplier = photoModel.Supplier;
            existing.Description = photoModel.Description;
            existing.Notes = photoModel.Notes;
            // pokud používáš ImagePath a chceš, aby se měnila z formuláře, můžeš nastavit:
            existing.ImagePath = photoModel.ImagePath;

            existing.UpdatedAt = DateTime.UtcNow;

            _context.Update(existing);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.Photos.Any(e => e.Id == photoModel.Id))
                return NotFound();
            else
                throw;
        }
        return RedirectToAction(nameof(Index));
    }

    // např. v PhotosController
    [HttpGet]
    [AllowAnonymous]
    [Route("diag/dbinfo")]
    public async Task<IActionResult> DiagDbInfo([FromServices] AppDbContext ctx)
    {
        var conn = ctx.Database.GetDbConnection();
        var connStr = conn?.ConnectionString ?? "(no connection string)";
        var dataSource = "(unknown)";
        try
        {
            dataSource = connStr.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
                ? /* parsuj Data Source */ connStr
                : Path.Combine(AppContext.BaseDirectory, "photoapp.db");
        }
        catch { }

        var count = 0;
        try { count = await ctx.Photos.CountAsync(); } catch (Exception ex) { return Problem(ex.Message); }

        return Ok(new { connStr, dataSource, count });
    }

    // GET: Photos/Details/5
    [AllowAnonymous]
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null) return NotFound();

        var photo = await _context.Photos.FirstOrDefaultAsync(m => m.Id == id);
        if (photo == null) return NotFound();

        return View(photo);
    }

    // GET: Photos/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();

        var photo = await _context.Photos.FirstOrDefaultAsync(m => m.Id == id);
        if (photo == null) return NotFound();

        return View(photo);
    }

    // POST: Photos/Delete/5
    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var photo = await _context.Photos.FindAsync(id);
        if (photo != null)
        {
            if (!string.IsNullOrEmpty(photo.PhotoPath))
            {
                var filePath = Path.Combine(_env.WebRootPath, photo.PhotoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(filePath))
                {
                    System.IO.File.Delete(filePath);
                }
            }

            _context.Photos.Remove(photo);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }

    // --- Export CSV ---
    [HttpGet]
    public async Task<IActionResult> ExportCsv()
    {
        var data = await _context.Photos.OrderByDescending(x => x.UpdatedAt).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("Id;Name;Code;Type;Supplier;Notes;PhotoPath;UpdatedAt");

        foreach (var p in data)
        {
            sb.AppendLine($"{p.Id};{p.Name};{p.Code};{p.Type};{p.Supplier};{p.Notes};{p.PhotoPath};{p.UpdatedAt:yyyy-MM-dd HH:mm}");
        }

        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/csv", "vzorky.csv");
    }

    // --- Export JSON ---
    [HttpGet]
    public async Task<IActionResult> ExportJson()
    {
        var data = await _context.Photos.OrderByDescending(x => x.UpdatedAt).ToListAsync();
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json", "vzorky.json");
    }

    // --- Export ZIP (CSV + obrázky) ---
    // --- Export ZIP (CSV + obrázky) ---
    // --- Export ZIP (CSV + obrázky) ---
    [HttpGet]

    public async Task<IActionResult> ExportZip()
    {
        // Seřadíme podle ID (resp. pořadí importu), aby se obrázky nikdy nepřeházely
        var photos = await _context.Photos
            .OrderBy(p => p.Id)
            .ToListAsync();

        using var package = new ExcelPackage();
        var ws = package.Workbook.Worksheets.Add("Vzorky");

        // hlavičky podle importní šablony
        var headers = new[]
        {
        "Position","ExternalId","Supplier","OriginalName",
        "Material","Form","Filler","Color",
        "Description","MonthlyQuantity","MFI","Notes","Photo"
    };
        for (int c = 0; c < headers.Length; c++)
        {
            ws.Cells[1, c + 1].Value = headers[c];
            ws.Cells[1, c + 1].Style.Font.Bold = true;
        }

        // data + obrázky
        for (int i = 0; i < photos.Count; i++)
        {
            var p = photos[i];
            int row = i + 2;

            ws.Cells[row, 1].Value = p.Position;
            ws.Cells[row, 2].Value = p.ExternalId;
            ws.Cells[row, 3].Value = p.Supplier;
            ws.Cells[row, 4].Value = p.OriginalName;
            ws.Cells[row, 5].Value = p.Material;
            ws.Cells[row, 6].Value = p.Form;
            ws.Cells[row, 7].Value = p.Filler;
            ws.Cells[row, 8].Value = p.Color;
            ws.Cells[row, 9].Value = p.Description;
            ws.Cells[row, 10].Value = p.MonthlyQuantity;
            ws.Cells[row, 11].Value = p.Mfi;
            ws.Cells[row, 12].Value = p.Notes;

            // vložení obrázku do sloupce 13 (Photo)
            if (!string.IsNullOrEmpty(p.ImagePath))
            {
                var relative = p.ImagePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.Combine(_env.WebRootPath, relative);
                if (System.IO.File.Exists(fullPath))
                {
                    using var stream = System.IO.File.OpenRead(fullPath);
                    var pic = ws.Drawings.AddPicture($"img_{row}", stream);

                    // přesně do buňky M{row}
                    pic.From.Row = row - 1;    // EPPlus používá 0-based index
                    pic.From.Column = 12;      // sloupec M = 12 (0-based)
                    pic.SetSize(80, 80);       // velikost obrázku
                    ws.Row(row).Height = 60;   // nastavíme výšku řádku tak, aby obrázek seděl
                }
            }

        }

        // automatické zalamování a šířky
        ws.Cells[1, 1, photos.Count + 1, headers.Length].AutoFitColumns();
        ws.Column(13).Width = 15;

        var bytes = package.GetAsByteArray();
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "vzorky_s_obrazky.xlsx");
    }
}