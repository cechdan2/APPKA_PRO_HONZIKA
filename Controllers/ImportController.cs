using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using PhotoApp.Data;
using PhotoApp.Models;
// Přidejte tyto usingy nahoře souboru
using System.IO.Compression;


public partial class PhotosController : Controller
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<PhotosController> _logger;

    public PhotosController(AppDbContext context, IWebHostEnvironment env, ILogger<PhotosController> logger)
    {
        _context = context;
        _env = env;
        _logger = logger;
    }


    // POST: /Photos/Import
    // POST: /Photos/Import
    // Vložte tento kód jako metodu uvnitř stávající třídy PhotosController


    [HttpPost]
    [ValidateAntiForgeryToken]

    public async Task<IActionResult> Import(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            ModelState.AddModelError("", "Nahrajte .xlsx soubor s daty.");
            return View();
        }

        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        var imported = new List<PhotoRecord>();
        var warnings = new List<string>();

        using var ms = new MemoryStream();
        await excelFile.CopyToAsync(ms);
        ms.Position = 0;

        // 1) Extract all media from xl/media and save to wwwroot/uploads
        var mediaList = new List<string>();
        using (var zip = new ZipArchive(new MemoryStream(ms.ToArray()), ZipArchiveMode.Read, false))
        {
            foreach (var entry in zip.Entries
                                     .Where(e => e.FullName.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(e => e.FullName))
            {
                using var s = entry.Open();
                using var mem = new MemoryStream();
                await s.CopyToAsync(mem);
                var bytes = mem.ToArray();
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(entry.FullName)}";
                var savePath = Path.Combine(uploadsFolder, fileName);
                await System.IO.File.WriteAllBytesAsync(savePath, bytes);
                mediaList.Add(fileName);
            }
        }

        // 2) Read Excel rows and assign images sequentially
        ms.Position = 0;
        using var package = new ExcelPackage(ms);
        var ws = package.Workbook.Worksheets.FirstOrDefault();
        if (ws == null)
        {
            ModelState.AddModelError("", "Soubor neobsahuje žádný list.");
            return View();
        }

        int startRow = 2;
        int endRow = ws.Dimension?.End.Row ?? 1;
        int colNotes = 12; // L

        int imageIndex = 0;
        for (int row = startRow; row <= endRow; row++)
        {
            // Skip empty rows
            bool rowEmpty = Enumerable.Range(1, colNotes)
                .All(c => string.IsNullOrWhiteSpace(ws.Cells[row, c].Text));
            if (rowEmpty) continue;

            var rec = new PhotoRecord
            {
                Position = ws.Cells[row, 1].GetValue<string>()?.Trim(),
                ExternalId = ws.Cells[row, 2].GetValue<string>()?.Trim(),
                Supplier = ws.Cells[row, 3].GetValue<string>()?.Trim() ?? "",
                OriginalName = ws.Cells[row, 4].GetValue<string>()?.Trim() ?? "",
                Material = ws.Cells[row, 5].GetValue<string>()?.Trim(),
                Form = ws.Cells[row, 6].GetValue<string>()?.Trim(),
                Filler = ws.Cells[row, 7].GetValue<string>()?.Trim(),
                Color = ws.Cells[row, 8].GetValue<string>()?.Trim(),
                Description = ws.Cells[row, 9].GetValue<string>()?.Trim(),
                MonthlyQuantity = ws.Cells[row, 10].GetValue<string>()?.Trim(),
                Mfi = ws.Cells[row, 11].GetValue<string>()?.Trim(),
                Notes = ws.Cells[row, colNotes].GetValue<string>()?.Trim() ?? "",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Sequentially assign image from mediaList
            if (imageIndex < mediaList.Count)
            {
                var fileName = mediaList[imageIndex++];
                rec.PhotoFileName = fileName;
                rec.ImagePath = "/uploads/" + fileName;
            }

            imported.Add(rec);
        }

        if (imported.Any())
        {
            _context.Photos.AddRange(imported);
            await _context.SaveChangesAsync();
        }

        TempData["ImportResult"] = $"{imported.Count} záznamů importováno. Varování: {warnings.Count}";
        TempData["ImportWarnings"] = string.Join("\n", warnings);

        return RedirectToAction(nameof(Index));
    }
}