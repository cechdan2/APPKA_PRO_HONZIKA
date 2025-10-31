using Microsoft.AspNetCore.Mvc;
using OfficeOpenXml;
using PhotoApp.Data;
using PhotoApp.Models;
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

        // 🔹 1) Smazat starý obsah složky /wwwroot/temp/
        var tempRoot = Path.Combine(_env.WebRootPath, "temp");
        if (Directory.Exists(tempRoot))
        {
            try
            {
                Directory.Delete(tempRoot, true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Nepodařilo se smazat starý obsah složky temp.");
            }
        }
        Directory.CreateDirectory(tempRoot);

        // 🔹 2) Vytvořit novou podsložku pro tento import
        var tempGuid = Guid.NewGuid().ToString();
        var tempFolder = Path.Combine(tempRoot, tempGuid);
        Directory.CreateDirectory(tempFolder);

        var imported = new List<PhotoRecord>();
        var warnings = new List<string>();

        using var ms = new MemoryStream();
        await excelFile.CopyToAsync(ms);
        ms.Position = 0;

        // 🔹 3) Rozbalit celý XLSX do temp složky
        using (var zip = new ZipArchive(new MemoryStream(ms.ToArray()), ZipArchiveMode.Read, false))
        {
            foreach (var entry in zip.Entries)
            {
                var fullPath = Path.Combine(tempFolder, entry.FullName);
                var dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                if (!string.IsNullOrEmpty(entry.Name))
                {
                    using var s = entry.Open();
                    using var fs = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                    await s.CopyToAsync(fs);
                }
            }
        }

        // 🔹 4) Seznam obrázků z xl/media, seřazený podle názvu
        var mediaFolder = Path.Combine(tempFolder, "xl", "media");
        var mediaList = new List<string>();

        if (Directory.Exists(mediaFolder))
        {
            mediaList = Directory
                .GetFiles(mediaFolder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f =>
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    // Pokus o číselné řazení podle "image1", "image2" atd.
                    if (int.TryParse(new string(name.Where(char.IsDigit).ToArray()), out int num))
                        return num;
                    return int.MaxValue; // pokud nemá číslo, dá se na konec
                })
                .ThenBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            _logger.LogInformation($"📂 Načteno {mediaList.Count} obrázků ze složky {mediaFolder}");
            foreach (var img in mediaList)
                _logger.LogInformation($" -> {Path.GetFileName(img)}");
        }
        else
        {
            _logger.LogWarning($"⚠️ Složka {mediaFolder} neexistuje — v Excelu zřejmě nejsou vloženy žádné obrázky.");
        }


        // 🔹 5) Načtení dat z Excelu
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
        int colNotes = 12;
        int imageIndex = 0;

        for (int row = startRow; row <= endRow; row++)
        {
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

            if (imageIndex < mediaList.Count)
            {
                var sourcePath = mediaList[imageIndex++];
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(sourcePath)}";
                var destPath = Path.Combine(uploadsFolder, fileName);
                System.IO.File.Copy(sourcePath, destPath, true);

                rec.PhotoFileName = fileName;
                rec.ImagePath = "/uploads/" + fileName;
            }
            else
            {
                var defaultFileName = "no-image.png";
                rec.PhotoFileName = defaultFileName;
                rec.ImagePath = "/uploads/" + defaultFileName;
            }

            imported.Add(rec);
        }

        imported.Reverse();

        if (imported.Any())
        {
            _context.Photos.AddRange(imported);
            await _context.SaveChangesAsync();
        }

        TempData["TempExtractPath"] = $"/temp/{tempGuid}";
        TempData["ImportResult"] = $"{imported.Count} záznamů importováno. Varování: {warnings.Count}";
        TempData["ImportWarnings"] = string.Join("\n", warnings);

        return RedirectToAction(nameof(Index));
    }
}
