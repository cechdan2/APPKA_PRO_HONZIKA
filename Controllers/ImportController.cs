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

        // složka wwwroot/uploads
        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
        if (!Directory.Exists(uploadsFolder))
            Directory.CreateDirectory(uploadsFolder);

        var imported = new List<PhotoRecord>();
        var warnings = new List<string>();

        try
        {
            using (var ms = new MemoryStream())
            {
                await excelFile.CopyToAsync(ms);
                ms.Position = 0;

                // --- 1) extrakce všech medií z xl/media a jejich uložení ---
                var mediaList = new List<(byte[] Bytes, string FileName)>();
                using (var zip = new ZipArchive(new MemoryStream(ms.ToArray()), ZipArchiveMode.Read, false))
                {
                    var entries = zip.Entries
                                     .Where(e => e.FullName.StartsWith("xl/media/", StringComparison.OrdinalIgnoreCase))
                                     .OrderBy(e => e.FullName)
                                     .ToList();

                    foreach (var e in entries)
                    {
                        using (var s = e.Open())
                        using (var mem = new MemoryStream())
                        {
                            await s.CopyToAsync(mem);
                            var bytes = mem.ToArray();
                            var fileName = Path.GetFileName(e.FullName);

                            // uložení do wwwroot/uploads
                            var savePath = Path.Combine(uploadsFolder, fileName);
                            await System.IO.File.WriteAllBytesAsync(savePath, bytes);

                            mediaList.Add((bytes, fileName));
                        }
                    }
                }

                // --- 2) načtení Excelu pomocí EPPlus ---
                ms.Position = 0;
                using (var package = new ExcelPackage(ms))
                {
                    var ws = package.Workbook.Worksheets.FirstOrDefault();
                    if (ws == null)
                    {
                        ModelState.AddModelError("", "Soubor neobsahuje žádný list.");
                        return View();
                    }

                    int startRow = 2;
                    int endRow = ws.Dimension?.End.Row ?? 1;

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
                    var pictures = drawings.OfType<ExcelPicture>().ToList();

                    warnings.Add($"Worksheet '{ws.Name}': drawings={drawings.Count}, pictures={pictures.Count}, mediaFiles={mediaList.Count}");

                    for (int row = startRow; row <= endRow; row++)
                    {
                        // přeskočíme prázdné řádky
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

                        // --- najdeme obrázek pro tuto buňku/řádek ---
                        ExcelPicture? pic = pictures.FirstOrDefault(p =>
                        {
                            int picFromRow = p.From.Row + 1;
                            int picFromCol = p.From.Column + 1;
                            return picFromRow == row && picFromCol == colPhoto;
                        });

                        if (pic != null)
                        {
                            try
                            {
                                byte[]? imgBytes = null;
                                string ext = ".png";

                                // reflexní pokus získat bytes
                                try
                                {
                                    var picType = pic.GetType();
                                    var propImage = picType.GetProperty("Image")?.GetValue(pic);
                                    if (propImage != null)
                                    {
                                        var pb = propImage.GetType().GetProperty("ImageBytes")?.GetValue(propImage) as byte[]
                                            ?? propImage.GetType().GetProperty("Bytes")?.GetValue(propImage) as byte[];

                                        if (pb == null)
                                        {
                                            var mGet = propImage.GetType().GetMethod("GetBytes") ?? propImage.GetType().GetMethod("GetImageBytes") ?? propImage.GetType().GetMethod("ToArray");
                                            if (mGet != null)
                                            {
                                                var res = mGet.Invoke(propImage, null);
                                                if (res is byte[] bbb) pb = bbb;
                                            }
                                        }

                                        imgBytes = pb;
                                    }
                                }
                                catch { }

                                // fallback podle pořadí v mediaList
                                if ((imgBytes == null || imgBytes.Length == 0) && pictures.IndexOf(pic) >= 0)
                                {
                                    int picIndex = pictures.IndexOf(pic);
                                    if (picIndex < mediaList.Count)
                                    {
                                        imgBytes = mediaList[picIndex].Bytes;
                                        ext = Path.GetExtension(mediaList[picIndex].FileName) ?? ".png";
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
                            }
                            catch (Exception ex)
                            {
                                warnings.Add($"Řádek {row}: chyba ukládání obrázku - {ex.Message}");
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
