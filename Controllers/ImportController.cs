using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Drawing;
using PhotoApp.Data;
using PhotoApp.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
// Přidejte tyto usingy nahoře souboru
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;




public partial class PhotosController : Controller
{
	private readonly AppDbContext _context;
	private readonly IWebHostEnvironment _env;

	public PhotosController(AppDbContext context, IWebHostEnvironment env)
	{
		_context = context;
		_env = env;
	}


    // POST: /Photos/Import
    // POST: /Photos/Import
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            ModelState.AddModelError("", "Nahrajte .xlsx soubor s daty.");
            return View();
        }

        // --- Nastavení licence EPPlus (robustně přes reflexi, aby to fungovalo napříč verzemi) ---
        try
        {
            var excelPackageType = typeof(ExcelPackage);

            // 1) zkusit novější statickou property "License"
            var licenseProp = excelPackageType.GetProperty("License", BindingFlags.Public | BindingFlags.Static);
            if (licenseProp != null && licenseProp.CanWrite)
            {
                var licensePropType = licenseProp.PropertyType;

                // pokud property přímo přijímá enum OfficeOpenXml.LicenseContext (některé verze)
                if (licensePropType == typeof(OfficeOpenXml.LicenseContext))
                {
                    licenseProp.SetValue(null, OfficeOpenXml.LicenseContext.NonCommercial);
                }
                else
                {
                    // vytvořit instanci typu property a nastavit v ní Context pokud existuje
                    var licenseInstance = Activator.CreateInstance(licensePropType);
                    var contextProp = licensePropType.GetProperty("Context", BindingFlags.Public | BindingFlags.Instance);
                    if (contextProp != null && contextProp.CanWrite && contextProp.PropertyType == typeof(OfficeOpenXml.LicenseContext))
                    {
                        contextProp.SetValue(licenseInstance, OfficeOpenXml.LicenseContext.NonCommercial);
                    }
                    licenseProp.SetValue(null, licenseInstance);
                }
            }
            else
            {
                // 2) fallback na starší API: LicenseContext (pokud existuje a zapisatelné)
                var licenseContextProp = excelPackageType.GetProperty("LicenseContext", BindingFlags.Public | BindingFlags.Static);
                if (licenseContextProp != null && licenseContextProp.CanWrite)
                {
                    // nastavit staré LicenseContext
                    licenseContextProp.SetValue(null, OfficeOpenXml.LicenseContext.NonCommercial);
                }
                else
                {
                    // pokud nelze nastavit ani jedním způsobem, přidej varování (nepřerušujeme import)
                    // V některých verzích může být licence pouze pro čtení nebo nastavena jinak; pokud narazíte na chybu, nastavte to v Program.cs při startu
                    // Např. Console.WriteLine("EPPlus license property not writable - please configure license in startup.");
                }
            }
        }
        catch (Exception ex)
        {
            // varování, ale nepřerušujeme (import může selhat později pokud EPPlus skutečně vyžaduje nastavení)
            // můžete také vrátit View s chybou, pokud preferujete
            ModelState.AddModelError("", $"Pozor: nastavení licence EPPlus selhalo: {ex.Message}");
            // continue - EPPlus může i tak fungovat pokud licence již byla nastavena jinde
        }

        // --- pokračování importu ---
        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        var imported = new List<PhotoRecord>();
        var warnings = new List<string>();

        using (var stream = excelFile.OpenReadStream())
        using (var package = new ExcelPackage(stream))
        {
            var ws = package.Workbook.Worksheets.FirstOrDefault();
            if (ws == null)
            {
                ModelState.AddModelError("", "Soubor neobsahuje žádný list.");
                return View();
            }

            var startRow = 2;
            var endRow = ws.Dimension?.End.Row ?? 1;

            int colPozice = 1;      // A
            int colExternalId = 2;  // B
            int colSupplier = 3;    // C
            int colOriginalName = 4;// D
            int colMaterial = 5;    // E
            int colForm = 6;        // F
            int colFiller = 7;      // G
            int colColor = 8;       // H
            int colDescription = 9; // I
            int colMonthlyQuantity = 10; // J
            int colMfi = 11;        // K
            int colNotes = 12;      // L
            int colPhoto = 13;      // M

            // získej vložené obrázky
            var pictures = ws.Drawings.OfType<ExcelPicture>().ToList();

            for (int row = startRow; row <= endRow; row++)
            {
                // přeskočit prázdné řádky
                bool rowEmpty = true;
                for (int c = 1; c <= colNotes; c++)
                {
                    if (!string.IsNullOrWhiteSpace(ws.Cells[row, c].Text))
                    {
                        rowEmpty = false;
                        break;
                    }
                }
                if (rowEmpty) continue;

                var rec = new PhotoRecord
                {
                    Position = ws.Cells[row, colPozice].GetValue<string>()?.Trim(),
                    ExternalId = ws.Cells[row, colExternalId].GetValue<string>()?.Trim(),
                    Supplier = ws.Cells[row, colSupplier].GetValue<string>()?.Trim() ?? "",
                    OriginalName = ws.Cells[row, colOriginalName].GetValue<string>()?.Trim() ?? "",
                    Material = ws.Cells[row, colMaterial].GetValue<string>()?.Trim(),
                    Form = ws.Cells[row, colForm].GetValue<string>()?.Trim(),
                    Filler = ws.Cells[row, colFiller].GetValue<string>()?.Trim(),
                    Color = ws.Cells[row, colColor].GetValue<string>()?.Trim(),
                    Description = ws.Cells[row, colDescription].GetValue<string>()?.Trim(),
                    MonthlyQuantity = ws.Cells[row, colMonthlyQuantity].GetValue<string>()?.Trim(),
                    Mfi = ws.Cells[row, colMfi].GetValue<string>()?.Trim(),
                    Notes = ws.Cells[row, colNotes].GetValue<string>()?.Trim() ?? "",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                ExcelPicture? pic = pictures.FirstOrDefault(p =>
                {
                    int picFromRow = p.From.Row + 1;
                    int picFromCol = p.From.Column + 1;
                    return picFromRow == row && picFromCol == colPhoto;
                });

                if (pic == null)
                {
                    pic = pictures.FirstOrDefault(p =>
                    {
                        int fromRow = p.From.Row + 1;
                        int fromCol = p.From.Column + 1;
                        int toRow = p.To.Row + 1;
                        int toCol = p.To.Column + 1;
                        return row >= fromRow && row <= toRow && colPhoto >= fromCol && colPhoto <= toCol;
                    });
                }

                if (pic != null)
                {
                    try
                    {
                        byte[]? imgBytes = null;
                        string ext = ".png";

                        object? imageObj = (object?)pic.Image;

                        if (imageObj is System.Drawing.Image sysImg)
                        {
                            using (var msImg = new MemoryStream())
                            {
                                var rawFormat = sysImg.RawFormat;
                                if (ImageFormat.Jpeg.Equals(rawFormat)) ext = ".jpg";
                                else if (ImageFormat.Png.Equals(rawFormat)) ext = ".png";
                                else if (ImageFormat.Gif.Equals(rawFormat)) ext = ".gif";
                                else ext = ".png";

                                sysImg.Save(msImg, rawFormat);
                                imgBytes = msImg.ToArray();
                            }
                        }
                        else if (imageObj != null)
                        {
                            var propBytes = imageObj.GetType().GetProperty("Bytes")?.GetValue(imageObj) as byte[];
                            if (propBytes != null)
                            {
                                imgBytes = propBytes;
                                var propContentType = imageObj.GetType().GetProperty("ContentType")?.GetValue(imageObj) as string;
                                if (!string.IsNullOrWhiteSpace(propContentType))
                                {
                                    if (propContentType.Contains("jpeg") || propContentType.Contains("jpg")) ext = ".jpg";
                                    else if (propContentType.Contains("png")) ext = ".png";
                                    else if (propContentType.Contains("gif")) ext = ".gif";
                                }
                            }
                            else
                            {
                                var m = imageObj.GetType().GetMethod("GetBytes") ?? imageObj.GetType().GetMethod("ToArray");
                                if (m != null)
                                {
                                    var result = m.Invoke(imageObj, null);
                                    if (result is byte[] bb) imgBytes = bb;
                                }
                            }
                        }

                        if (imgBytes != null && imgBytes.Length > 0)
                        {
                            var fileName = $"{Guid.NewGuid()}{ext}";
                            var filePath = Path.Combine(uploadsFolder, fileName);
                            await System.IO.File.WriteAllBytesAsync(filePath, imgBytes);

                            rec.PhotoFileName = fileName;
                            rec.ImagePath = "/uploads/" + fileName;
                        }
                        else
                        {
                            warnings.Add($"Řádek {row}: obrázek nalezen, ale nelze získat byty (neznámý typ obrázku).");
                        }
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Řádek {row}: chyba ukládání obrázku - {ex.Message}");
                    }
                }
                else
                {
                    var photoCellText = ws.Cells[row, colPhoto].GetValue<string>()?.Trim();
                    if (!string.IsNullOrEmpty(photoCellText))
                    {
                        if (Uri.IsWellFormedUriString(photoCellText, UriKind.Absolute))
                        {
                            rec.ImagePath = photoCellText;
                            rec.PhotoFileName = Path.GetFileName(photoCellText);
                        }
                        else
                        {
                            rec.PhotoFileName = photoCellText;
                            warnings.Add($"Řádek {row}: fotka uvedena jako text ({photoCellText}), nebyla zkopírována.");
                        }
                    }
                }

                imported.Add(rec);
            }
        }

        if (imported.Any())
        {
            _context.Photos.AddRange(imported);
            await _context.SaveChangesAsync();
        }

        TempData["ImportResult"] = $"{imported.Count} záznamů importováno. Varování: {warnings.Count}";
        TempData["ImportWarnings"] = string.Join(" | ", warnings);

        return RedirectToAction(nameof(Index));
    }
}