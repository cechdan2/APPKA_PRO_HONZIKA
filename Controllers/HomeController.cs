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
                ModelState.AddModelError("imageFile", "Nepodporovaný formát obrázku.");
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

    // GET: /Home/Details/{id}  (pøihlášený uživatel, zobrazí QR kód odkazující na veøejný detail)
    public async Task<IActionResult> Details(int id)
    {
        var photo = await _context.Photos.FindAsync(id);
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
                _logger.LogError(ex, "Generování QR SVG selhalo pro url {Url}", publicUrl);
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
        var photo = await _context.Photos.FindAsync(id);
        if (photo == null)
            return NotFound();

        // pùvodnì
        // return View("DetailsAnonymous", photo);

        // nahraïte tímto:
        return View("~/Views/Photos/DetailsAnonymous.cshtml", photo);
    }

    // Pomocná metoda: vytvoøí SVG string QR kódu (SvgQRCode) - cross-platform
    private string GenerateQrSvg(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
            throw new ArgumentNullException(nameof(payload));

        using var qrGen = new QRCodeGenerator();
        using var qrData = qrGen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var svgQr = new SvgQRCode(qrData);
        return svgQr.GetGraphic(4); // vrací SVG string
    }
}