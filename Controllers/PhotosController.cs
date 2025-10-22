using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoApp.Data;
using PhotoApp.Models;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;

namespace PhotoApp.Controllers;

[Authorize] // celý kontroler chráněn; pokud chcete veřejný index, přidejte [AllowAnonymous] nad Index()
public class PhotosController : Controller
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;
    private const long MaxFileSize = 5 * 1024 * 1024; // 5 MB - upravte dle potřeby
    private static readonly string[] PermittedTypes = { "image/jpeg", "image/png", "image/gif", "image/webp" };

    public PhotosController(AppDbContext context, IWebHostEnvironment env)
    {
        _context = context;
        _env = env;
    }


    public IActionResult Import()
    {
        return View(); // vrátí Views/Photos/Import.cshtml
    }

    // GET: Photos
    [AllowAnonymous]
    public async Task<IActionResult> Index(string? search)
    {
        var photos = _context.Photos.AsQueryable();

        if (!string.IsNullOrEmpty(search))
        {
            var s = search.ToLower();
            photos = photos.Where(p => EF.Functions.Like(p.Name.ToLower(), $"%{s}%")
                                    || EF.Functions.Like(p.Code.ToLower(), $"%{s}%"));
        }

        photos = photos.OrderByDescending(p => p.UpdatedAt);

        return View(await photos.ToListAsync());
    }

    // GET: Photos/Create
    public IActionResult Create() => View();

    // POST: Photos/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,Code,Type,Supplier,Notes")] PhotoRecord photo, IFormFile? PhotoFile)
    {
        if (!ModelState.IsValid)
            return View(photo);

        photo.UpdatedAt = DateTime.UtcNow;
        photo.CreatedAt = DateTime.UtcNow;

        // Uložení fotky do wwwroot/uploads
        if (PhotoFile != null && PhotoFile.Length > 0)
        {
            if (PhotoFile.Length > MaxFileSize)
            {
                ModelState.AddModelError("PhotoFile", "Soubor je příliš velký.");
                return View(photo);
            }

            if (!PermittedTypes.Contains(PhotoFile.ContentType))
            {
                ModelState.AddModelError("PhotoFile", "Nepodporovaný typ souboru.");
                return View(photo);
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

            photo.PhotoPath = "/uploads/" + fileName;
        }

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
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Code,Type,Supplier,Notes,PhotoPath")] PhotoRecord photo, IFormFile? PhotoFile)
    {
        if (id != photo.Id)
            return NotFound();

        if (!ModelState.IsValid)
            return View(photo);

        try
        {
            // načtěte aktuální záznam z DB pro zjištění staré cesty
            var existing = await _context.Photos.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (existing == null) return NotFound();

            // pokud je nový soubor, nahradíme a smažeme starý
            if (PhotoFile != null && PhotoFile.Length > 0)
            {
                if (PhotoFile.Length > MaxFileSize)
                {
                    ModelState.AddModelError("PhotoFile", "Soubor je příliš velký.");
                    return View(photo);
                }

                if (!PermittedTypes.Contains(PhotoFile.ContentType))
                {
                    ModelState.AddModelError("PhotoFile", "Nepodporovaný typ souboru.");
                    return View(photo);
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

                // smaž starý soubor pokud existuje
                if (!string.IsNullOrEmpty(existing.PhotoPath))
                {
                    var oldPath = Path.Combine(_env.WebRootPath, existing.PhotoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(oldPath))
                    {
                        System.IO.File.Delete(oldPath);
                    }
                }

                photo.PhotoPath = "/uploads/" + fileName;
            }
            else
            {
                // pokud nebyl nahrán nový soubor, zachovej starou cestu z DB
                photo.PhotoPath = existing.PhotoPath;
            }

            photo.UpdatedAt = DateTime.UtcNow;

            _context.Update(photo);
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!_context.Photos.Any(e => e.Id == photo.Id))
                return NotFound();
            else
                throw;
        }
        return RedirectToAction(nameof(Index));
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
            // smaž soubor z disku, pokud existuje
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
    [HttpGet]
    public async Task<IActionResult> ExportZip()
    {
        var photos = await _context.Photos.OrderByDescending(x => x.UpdatedAt).ToListAsync();

        // 1. Vygeneruj CSV do paměti
        var sb = new StringBuilder();
        sb.AppendLine("Id;Name;Code;Type;Supplier;Notes;PhotoPath;UpdatedAt");
        foreach (var p in photos)
            sb.AppendLine($"{p.Id};{p.Name};{p.Code};{p.Type};{p.Supplier};{p.Notes};{Path.GetFileName(p.PhotoPath)};{p.UpdatedAt:yyyy-MM-dd HH:mm}");

        using (var ms = new MemoryStream())
        {
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                // 2. Přidej CSV
                var csvEntry = zip.CreateEntry("vzorky.csv");
                using (var entryStream = csvEntry.Open())
                using (var sw = new StreamWriter(entryStream, Encoding.UTF8))
                    sw.Write(sb.ToString());

                // 3. Přidej obrázky
                foreach (var p in photos)
                {
                    if (!string.IsNullOrEmpty(p.PhotoPath))
                    {
                        var filePath = Path.Combine(_env.WebRootPath, p.PhotoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (System.IO.File.Exists(filePath))
                        {
                            var entry = zip.CreateEntry("uploads/" + Path.GetFileName(p.PhotoPath));
                            using (var fileStream = System.IO.File.OpenRead(filePath))
                            using (var zipStream = entry.Open())
                                await fileStream.CopyToAsync(zipStream);
                        }
                    }
                }
            }

            // reset pozice není nutná pro ToArray(), ale je ok
            return File(ms.ToArray(), "application/zip", "vzorky.zip");
        }
    }
}