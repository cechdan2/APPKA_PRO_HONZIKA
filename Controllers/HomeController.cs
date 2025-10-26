using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoApp.Data;
using PhotoApp.Models;
using QRCoder;

namespace PhotoApp.Controllers;
[Authorize]
public class HomeController : Controller
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<HomeController> _logger;

    public HomeController(AppDbContext context, IWebHostEnvironment env, ILogger<HomeController> logger)
    {
        _context = context;
        _env = env;
        _logger = logger;
    }

    // GET: /
    public async Task<IActionResult> Index()
    {
        var photos = await _context.Photos.OrderByDescending(p => p.CreatedAt).ToListAsync();
        return View(photos);
    }

    // GET: /Home/Create
    public IActionResult Create() => View();

    // POST: /Home/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PhotoRecord record, IFormFile? imageFile)
    {
        if (!ModelState.IsValid)
            return View(record);

        if (imageFile != null && imageFile.Length > 0)
        {
            var permitted = new[] { "image/jpeg", "image/png", "image/gif", "image/webp" };
            if (!permitted.Contains(imageFile.ContentType))
            {
                ModelState.AddModelError("imageFile", "Nepodporovan� form�t obr�zku.");
                return View(record);
            }

            var uploads = Path.Combine(_env.WebRootPath, "uploads");
            if (!Directory.Exists(uploads))
                Directory.CreateDirectory(uploads);

            var fileName = Path.GetFileNameWithoutExtension(imageFile.FileName)
                           + "_" + Guid.NewGuid().ToString("N")
                           + Path.GetExtension(imageFile.FileName);
            var path = Path.Combine(uploads, fileName);

            using (var stream = new FileStream(path, FileMode.Create))
            {
                await imageFile.CopyToAsync(stream);
            }

            record.PhotoPath = "/uploads/" + fileName;
        }

        record.CreatedAt = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        _context.Add(record);
        await _context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }
    // Replace the existing ClearPhotos method with this implementation
    // POST: /Home/ClearPhotos
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearPhotos()
    {
        try
        {
            // 1) Sma�eme v�echny z�znamy v DB (tabulka Photos)
            var allPhotos = await _context.Photos.ToListAsync();
            if (allPhotos.Any())
            {
                _context.Photos.RemoveRange(allPhotos);
                await _context.SaveChangesAsync();
            }

            // 2) Sma�eme v�echny soubory a podslo�ky v wwwroot/uploads
            var uploadsDir = Path.Combine(_env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "uploads");
            try
            {
                if (Directory.Exists(uploadsDir))
                {
                    // sma�eme celou slo�ku uploads a znovu ji vytvo��me �istou
                    Directory.Delete(uploadsDir, recursive: true);
                }
                // v�dy vytvo��me pr�zdnou slo�ku uploads (aplikace o�ek�v� jej� existenci)
                Directory.CreateDirectory(uploadsDir);
            }
            catch (Exception ex)
            {
                // logovat pokud m� _logger, jinak ulo��me do TempData
                try { _logger?.LogWarning(ex, "Failed to clear/create uploads directory"); } catch { }
                TempData["Error"] = "Slo�ku uploads se nepoda�ilo pln� vy�istit: " + ex.Message;
                return RedirectToAction("Index", "Photos");
            }

            // 3) Natvrdo smazat SQLite soubor datab�ze (a -wal, -shm)
            var dbPath = Path.Combine(AppContext.BaseDirectory, "photoapp.db");
            var walPath = dbPath + "-wal";
            var shmPath = dbPath + "-shm";

            // Pokus�me se zav��t p�ipojen� DbContextu, aby nebyl soubor zablokovan�
            try
            {
                await _context.Database.CloseConnectionAsync();
            }
            catch
            {
                // ignorujeme chybu p�i zav�r�n�, pokus�me se smazat p�es retry
            }

            bool dbDeleted = false;
            if (System.IO.File.Exists(dbPath))
            {
                const int maxAttempts = 8;
                for (int attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        // uvoln�n� p��padn�ch nedokon�en�ch stream�
                        GC.Collect();
                        GC.WaitForPendingFinalizers();

                        System.IO.File.Delete(dbPath);
                        dbDeleted = true;
                        break;
                    }
                    catch (IOException ioEx)
                    {
                        try { _logger?.LogWarning(ioEx, "Attempt {Attempt} to delete DB file failed (in use).", attempt); } catch { }
                        // �ek�me a zkus�me znovu
                        await Task.Delay(300 * attempt);
                    }
                    catch (UnauthorizedAccessException uaEx)
                    {
                        try { _logger?.LogWarning(uaEx, "Attempt {Attempt} to delete DB file failed (access).", attempt); } catch { }
                        await Task.Delay(300 * attempt);
                    }
                    catch (Exception ex)
                    {
                        try { _logger?.LogError(ex, "Unexpected error when deleting DB file"); } catch { }
                        break;
                    }
                }
            }
            else
            {
                // pokud soubor neexistuje, pova�ujeme to za �sp�ch (u� je smazan�)
                dbDeleted = true;
            }

            // Pokus�me se smazat -wal a -shm soubory (ignorujeme chyby)
            try { if (System.IO.File.Exists(walPath)) System.IO.File.Delete(walPath); } catch (Exception ex) { _logger?.LogWarning(ex, "Failed to delete wal file"); }
            try { if (System.IO.File.Exists(shmPath)) System.IO.File.Delete(shmPath); } catch (Exception ex) { _logger?.LogWarning(ex, "Failed to delete shm file"); }

            if (!dbDeleted)
            {
                TempData["Error"] = "Datab�zov� soubor se nepoda�ilo smazat (pravd�podobn� je zam�en). Zastav aplikaci a zkuste to znovu.";
                return RedirectToAction("Index", "Photos");
            }

            TempData["Message"] = "V�echny z�znamy v tabulce Photos byly odstran�ny, uploads byla vypr�zdn�na a datab�zov� soubor byl smaz�n.";
        }
        catch (Exception ex)
        {
            // obecn� zachycen� chyb
            try { _logger?.LogError(ex, "Error in ClearPhotos"); } catch { }
            TempData["Error"] = "Chyba p�i maz�n�: " + ex.Message;
        }

        return RedirectToAction("Index", "Photos");
    }


    // GET: /Home/Details/{id}  (p�ihl�en� u�ivatel, zobraz� QR k�d odkazuj�c� na ve�ejn� detail)
    public async Task<IActionResult> Details(int id)
    {
        var photo = await _context.Photos.FindAsync(id);
        if (photo == null)
            return NotFound();

        // bezpe�n� sestav�me publicUrl; pou�ijeme Context (dostupn� v Razor view i tady)
        var scheme = Request?.Scheme ?? "https";
        var publicUrl = Url.Action("DetailsAnonymous", "Photos", new { id = photo.Id }, scheme);
        ViewBag.PublicUrl = publicUrl;

        // pokud publicUrl je platn� string, zkus�me vygenerovat SVG; jinak ponech�me null a pou�ijeme fallback
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            try
            {
                ViewBag.PublicQrSvg = GenerateQrSvg(publicUrl);
                ViewBag.PublicQrError = null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Generov�n� QR SVG selhalo pro url {Url}", publicUrl);
                ViewBag.PublicQrSvg = null;
                ViewBag.PublicQrError = ex.Message;
            }
        }
        else
        {
            ViewBag.PublicQrSvg = null;
            ViewBag.PublicQrError = "Public URL je pr�zdn�.";
        }

        return View(photo);
    }

    // GET: /Home/DetailsAnonymous/{id}
    [AllowAnonymous]
    public async Task<IActionResult> DetailsAnonymous(int id)
    {
        var photo = await _context.Photos.FindAsync(id);
        if (photo == null)
            return NotFound();

        // p�vodn�
        // return View("DetailsAnonymous", photo);

        // nahra�te t�mto:
        return View("~/Views/Photos/DetailsAnonymous.cshtml", photo);
    }

    // Pomocn� metoda: vytvo�� SVG string QR k�du (SvgQRCode) - cross-platform
    private string GenerateQrSvg(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentNullException(nameof(payload));

        using var qrGen = new QRCodeGenerator();
        using var qrData = qrGen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var svgQr = new SvgQRCode(qrData);
        return svgQr.GetGraphic(4); // vrac� SVG string
    }
}