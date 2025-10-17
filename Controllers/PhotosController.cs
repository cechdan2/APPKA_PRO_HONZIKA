using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoApp.Data;
using PhotoApp.Models;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using Microsoft.AspNetCore.Authorization;

[Authorize] // <-- přidat na kontroler!
public class PhotosController : Controller
{
    private readonly AppDbContext _context;

    public PhotosController(AppDbContext context)
    {
        _context = context;
    }

    // GET: Photos
    public async Task<IActionResult> Index(string search)
    {
        var photos = from p in _context.Photos
                     select p;

        if (!string.IsNullOrEmpty(search))
        {
            photos = photos.Where(p => p.Name.Contains(search) || p.Code.Contains(search));
        }

        photos = photos.OrderByDescending(p => p.UpdatedAt);

        return View(await photos.ToListAsync());
    }

    // GET: Photos/Create
    public IActionResult Create()
    {
        return View();
    }

    // POST: Photos/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,Code,Type,Supplier,Notes,UpdatedAt,PhotoPath")] PhotoRecord photo, IFormFile PhotoFile)
    {
        if (ModelState.IsValid)
        {
            photo.UpdatedAt = DateTime.Now;

            // Uložení fotky do wwwroot/uploads
            if (PhotoFile != null && PhotoFile.Length > 0)
            {
                var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
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
        return View(photo);
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
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Code,Type,Supplier,Notes,UpdatedAt,PhotoPath")] PhotoRecord photo, IFormFile? PhotoFile)
    {
        if (id != photo.Id)
            return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                photo.UpdatedAt = DateTime.Now;

                // Uložení nové fotky, pokud je vybrána
                if (PhotoFile != null && PhotoFile.Length > 0)
                {
                    var uploads = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/uploads");
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
        return View(photo);
    }

    // GET: Photos/Details/5
    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();

        var photo = await _context.Photos.FirstOrDefaultAsync(m => m.Id == id);
        if (photo == null)
            return NotFound();

        return View(photo);
    }

    // GET: Photos/Delete/5
    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();

        var photo = await _context.Photos.FirstOrDefaultAsync(m => m.Id == id);
        if (photo == null)
            return NotFound();

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
            sb.AppendLine($"{p.Id};{p.Name};{p.Code};{p.Type};{p.Supplier};{p.Notes};{System.IO.Path.GetFileName(p.PhotoPath)};{p.UpdatedAt:yyyy-MM-dd HH:mm}");

        using (var ms = new MemoryStream())
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
                    var filePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", p.PhotoPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(filePath))
                    {
                        var entry = zip.CreateEntry("uploads/" + System.IO.Path.GetFileName(p.PhotoPath));
                        using (var fileStream = System.IO.File.OpenRead(filePath))
                        using (var zipStream = entry.Open())
                            await fileStream.CopyToAsync(zipStream);
                    }
                }
            }

            zip.Dispose();
            return File(ms.ToArray(), "application/zip", "vzorky.zip");
        }
    }
}