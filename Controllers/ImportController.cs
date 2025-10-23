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
using System.IO.Compression;
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

    try { OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial; } catch { }

    var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
    if (!Directory.Exists(uploadsFolder))
        Directory.CreateDirectory(uploadsFolder);

    var imported = new List<PhotoRecord>();
    var warnings = new List<string>();

    try
    {
        // načteme celý upload do paměti (můžeme ho použít pro EPPlus i pro ZIP)
        using (var ms = new MemoryStream())
        {
            await excelFile.CopyToAsync(ms);
            ms.Position = 0;

            // 1) EXTRAKCE media souborů z xl/media/ (ZIP)
            var mediaList = new List<(byte[] Bytes, string FileName)>();
            using (var zip = new ZipArchive(new MemoryStream(ms.ToArray()), ZipArchiveMode.Read, false))
            {
                // najdeme všechny položky, které jsou v xl/media/
                var entries = zip.Entries
                                 .Where(e => e.FullName.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(e => e.FullName) // image1.png, image2.jpeg ...
                                 .ToList();

                foreach (var e in entries)
                {
                    using (var s = e.Open())
                    using (var mem = new MemoryStream())
                    {
                        await s.CopyToAsync(mem);
                        mediaList.Add((mem.ToArray(), Path.GetFileName(e.FullName)));
                    }
                }
            }

            // 2) Otevřeme EPPlus ze stejného streamu (resetujeme pozici)
            ms.Position = 0;
            using (var package = new OfficeOpenXml.ExcelPackage(ms))
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

                var drawings = ws.Drawings.ToList();
                var pictures = drawings.OfType<OfficeOpenXml.Drawing.ExcelPicture>().ToList();
                warnings.Add($"Worksheet '{ws.Name}': drawings={drawings.Count}, pictures={pictures.Count}, mediaFiles={mediaList.Count}");

                // vytvoříme mapu index->media podle pořadí; použijeme pokud nelze extrahovat bytes z pic
                // Pozn.: v mnoha xlsx souborech pořadí picture kolekce odpovídá imageN pořadí v xl/media
                var mediaByIndex = mediaList;

                for (int row = startRow; row <= endRow; row++)
                {
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

                    // najít obrázek pro tuto buňku/řádek
                    OfficeOpenXml.Drawing.ExcelPicture? pic = pictures.FirstOrDefault(p =>
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

                            // 3A) POKUSÍME SE z ExcelPicture získat bajty (některé verze EPPlus poskytují Image/Bytes)
                            try
                            {
                                // reflexní pokusy (nejsou závislé na konkrétní property name; zkusíme několik variant)
                                var picType = pic.GetType();

                                // common: vlastnost "Image" může vrátit interní objekt; ten může mít "ImageBytes" / "Bytes" / "GetBytes" atd.
                                var propImage = picType.GetProperty("Image")?.GetValue(pic);
                                if (propImage != null)
                                {
                                    // zkusíme vlastnosti Bytes / ImageBytes
                                    var pb = propImage.GetType().GetProperty("ImageBytes")?.GetValue(propImage) as byte[];
                                    if (pb == null) pb = propImage.GetType().GetProperty("Bytes")?.GetValue(propImage) as byte[];

                                    if (pb == null)
                                    {
                                        var mGet = propImage.GetType().GetMethod("GetBytes") ?? propImage.GetType().GetMethod("GetImageBytes") ?? propImage.GetType().GetMethod("ToArray");
                                        if (mGet != null)
                                        {
                                            var res = mGet.Invoke(propImage, null);
                                            if (res is byte[] bb) pb = bb;
                                        }
                                    }

                                    imgBytes = pb;
                                }

                                // další fallback: samotný pic může mít Bytes/ImageBytes/GetBytes
                                if (imgBytes == null)
                                {
                                    var maybe = picType.GetProperty("ImageBytes")?.GetValue(pic) as byte[]
                                                ?? picType.GetProperty("Bytes")?.GetValue(pic) as byte[];
                                    if (imgBytes == null)
                                    {
                                        var m = picType.GetMethod("GetBytes") ?? picType.GetMethod("ToArray") ?? picType.GetMethod("GetImageData");
                                        if (m != null)
                                        {
                                            var res = m.Invoke(pic, null);
                                            if (res is byte[] b2) imgBytes = b2;
                                        }
                                    }
                                }

                                // někdy lze získat System.Drawing.Image přímo
                                if (imgBytes == null)
                                {
                                    var propSysImg = picType.GetProperty("Image")?.GetValue(pic) as System.Drawing.Image;
                                    if (propSysImg != null)
                                    {
                                        using (var msImg = new MemoryStream())
                                        {
                                            propSysImg.Save(msImg, propSysImg.RawFormat);
                                            imgBytes = msImg.ToArray();
                                        }
                                    }
                                }
                            }
                            catch
                            {
                                // ignorujeme chyby reflexe zde; použijeme fallback níže
                            }

                            // 3B) pokud se nepodařilo, použijeme fallback z xl/media podle indexu
                            if ((imgBytes == null || imgBytes.Length == 0) && pictures.IndexOf(pic) >= 0)
                            {
                                int picIndex = pictures.IndexOf(pic); // 0-based
                                if (picIndex < mediaByIndex.Count)
                                {
                                    imgBytes = mediaByIndex[picIndex].Bytes;
                                    var mediaFileName = mediaByIndex[picIndex].FileName;
                                    ext = Path.GetExtension(mediaFileName) ?? ".png";
                                }
                            }

                            // další fallback: pokud stále žádné bajty, pokusíme se najít první media (bezpečnostně)
                            if ((imgBytes == null || imgBytes.Length == 0) && mediaByIndex.Any())
                            {
                                imgBytes = mediaByIndex.First().Bytes;
                                ext = Path.GetExtension(mediaByIndex.First().FileName) ?? ".png";
                                warnings.Add($"Řádek {row}: použito první media jako fallback.");
                            }

                            // uložení souboru
                            if (imgBytes != null && imgBytes.Length > 0)
                            {
                                if (imgBytes.Length >= 4)
                                {
                                    if (imgBytes[0] == 0xFF && imgBytes[1] == 0xD8) ext = ".jpg";
                                    else if (imgBytes[0] == 0x89 && imgBytes[1] == 0x50) ext = ".png";
                                    else if (imgBytes[0] == 0x47 && imgBytes[1] == 0x49) ext = ".gif";
                                    else if (imgBytes[0] == 0x42 && imgBytes[1] == 0x4D) ext = ".bmp";
                                }

                                var fileName = $"{Guid.NewGuid()}{ext}";
                                var filePath = Path.Combine(uploadsFolder, fileName);
                                await System.IO.File.WriteAllBytesAsync(filePath, imgBytes);

                                rec.PhotoFileName = fileName;
                                rec.ImagePath = "/uploads/" + fileName;
                            }
                            else
                            {
                                warnings.Add($"Řádek {row}: obrázek nalezen, ale nepodařilo se získat bajty (typ: {pic.GetType().FullName}).");
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
                } // for rows
            } // using package
        } // using ms
    }
    catch (Exception exOuter)
    {
        warnings.Add("Exception při čtení souboru: " + exOuter.Message);
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