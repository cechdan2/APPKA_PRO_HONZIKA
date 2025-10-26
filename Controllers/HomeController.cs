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
                ModelState.AddModelError("imageFile", "Nepodporovan� form�t obr�zku.");
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
            // 1) Sma�eme v�echny z�znamy v DB (tabulka Photos)
            var allPhotos = await context.Photos.ToListAsync();
            if (allPhotos.Count > 0)
            {
                context.Photos.RemoveRange(allPhotos);
                await context.SaveChangesAsync();
            }

            // 2) Sma�eme v�echny soubory a podslo�ky v wwwroot/uploads
            var uploadsDir = Path.Combine(env.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot"), "uploads");
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
                logger.LogWarning(ex, "Failed to clear/create uploads directory");
                TempData["Error"] = "Slo�ku uploads se nepoda�ilo pln� vy�istit: " + ex.Message;
                return RedirectToAction("Index", "Photos");
            }

            TempData["Message"] = "V�echny z�znamy z tabulky Photos byly odstran�ny a slo�ka uploads byla vypr�zdn�na.";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ClearPhotos");
            TempData["Error"] = "Chyba p�i maz�n�: " + ex.Message;
        }

        return RedirectToAction("Index", "Photos");
    }


    // GET: /Home/Details/{id}  (p�ihl�en� u�ivatel, zobraz� QR k�d odkazuj�c� na ve�ejn� detail)
    public async Task<IActionResult> Details(int id)
    {
        var photo = await context.Photos.FindAsync(id);
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
                logger.LogError(ex, "Generov�n� QR SVG selhalo pro url {Url}", publicUrl);
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
        var photo = await context.Photos.FindAsync(id);
        if (photo == null)
            return NotFound();

        return View("~/Views/Photos/DetailsAnonymous.cshtml", photo);
    }

    // Pomocn� metoda: vytvo�� SVG string QR k�du (SvgQRCode) - cross-platform
    private static string GenerateQrSvg(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentNullException(nameof(payload));

        using var qrGen = new QRCodeGenerator();
        using var qrData = qrGen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var svgQr = new SvgQRCode(qrData);
        return svgQr.GetGraphic(4); // vrac� SVG string
    }
}