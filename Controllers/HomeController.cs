using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PhotoApp.Data;
using PhotoApp.Models;
using QRCoder;

namespace PhotoApp.Controllers;

[Authorize]
public class HomeController(AppDbContext context, IWebHostEnvironment env, ILogger<HomeController> logger) : Controller
{
    // GET: /
    public async Task<IActionResult> Index()
    {
        var photos = await context.Photos.OrderByDescending(p => p.CreatedAt).ToListAsync();
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
                ModelState.AddModelError("imageFile", "Nepodporovaný formát obrázku.");
                return View(record);
            }

            var uploads = Path.Combine(env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "uploads");
            if (!Directory.Exists(uploads))
                Directory.CreateDirectory(uploads);

            var fileName = Path.GetFileNameWithoutExtension(imageFile.FileName)
                           + "_" + Guid.NewGuid().ToString("N")
                           + Path.GetExtension(imageFile.FileName);
            var path = Path.Combine(uploads, fileName);

            await using (var stream = new FileStream(path, FileMode.Create))
            {
                await imageFile.CopyToAsync(stream);
            }

            record.PhotoPath = "/uploads/" + fileName;
        }

        record.CreatedAt = DateTime.UtcNow;
        record.UpdatedAt = DateTime.UtcNow;

        context.Add(record);
        await context.SaveChangesAsync();

        return RedirectToAction(nameof(Index));
    }

    // Replace the existing ClearPhotos method with this implementation
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearPhotos()
    {
        try
        {
            // 1) Smažeme všechny záznamy v DB (tabulka Photos)
            var allPhotos = await context.Photos.ToListAsync();
            if (allPhotos.Count > 0)
            {
                context.Photos.RemoveRange(allPhotos);
                await context.SaveChangesAsync();
            }

            // 2) Smažeme všechny soubory a podsložky v wwwroot/uploads
            var uploadsDir = Path.Combine(env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "uploads");
            try
            {
                if (Directory.Exists(uploadsDir))
                {
                    // smažeme celou složku uploads a znovu ji vytvoøíme èistou
                    Directory.Delete(uploadsDir, recursive: true);
                }

                // vždy vytvoøíme prázdnou složku uploads (aplikace oèekává její existenci)
                Directory.CreateDirectory(uploadsDir);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clear/create uploads directory");
                TempData["Error"] = "Složku uploads se nepodaøilo plnì vyèistit: " + ex.Message;
                return RedirectToAction("Index", "Photos");
            }

            TempData["Message"] = "Všechny záznamy z tabulky Photos byly odstranìny a složka uploads byla vyprázdnìna.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ClearPhotos");
            TempData["Error"] = "Chyba pøi mazání: " + ex.Message;
        }

        return RedirectToAction("Index", "Photos");
    }


    // GET: /Home/Details/{id}  (pøihlášený uživatel, zobrazí QR kód odkazující na veøejný detail)
    public async Task<IActionResult> Details(int id)
    {
        var photo = await context.Photos.FindAsync(id);
        if (photo == null)
            return NotFound();

        // bezpeènì sestavíme publicUrl; použijeme Context (dostupné v Razor view i tady)
        var scheme = Request?.Scheme ?? "https";
        var publicUrl = Url.Action("DetailsAnonymous", "Photos", new { id = photo.Id }, scheme);
        ViewBag.PublicUrl = publicUrl;

        // pokud publicUrl je platný string, zkusíme vygenerovat SVG; jinak ponecháme null a použijeme fallback
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            try
            {
                ViewBag.PublicQrSvg = GenerateQrSvg(publicUrl);
                ViewBag.PublicQrError = null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Generování QR SVG selhalo pro url {Url}", publicUrl);
                ViewBag.PublicQrSvg = null;
                ViewBag.PublicQrError = ex.Message;
            }
        }
        else
        {
            ViewBag.PublicQrSvg = null;
            ViewBag.PublicQrError = "Public URL je prázdné.";
        }

        return View(photo);
    }

    // GET: /Home/DetailsAnonymous/{id}
    [AllowAnonymous]
    public async Task<IActionResult> DetailsAnonymous(int id)
    {
        var photo = await context.Photos.FindAsync(id);
        if (photo == null)
            return NotFound();

        return View("~/Views/Photos/DetailsAnonymous.cshtml", photo);
    }

    // Pomocná metoda: vytvoøí SVG string QR kódu (SvgQRCode) - cross-platform
    private static string GenerateQrSvg(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentNullException(nameof(payload));

        using var qrGen = new QRCodeGenerator();
        using var qrData = qrGen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var svgQr = new SvgQRCode(qrData);
        return svgQr.GetGraphic(4); // vrací SVG string
    }
}