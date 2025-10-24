using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhotoApp.Data;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace PhotoApp.Controllers
{
    // Doporučeno: přidat [Authorize(Roles = "Admin")] nad třídou v produkci
    public class BackupController : Controller
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<BackupController> _logger;
        private readonly string _defaultDbFileName = "photoapp.db";

        public BackupController(AppDbContext context, IWebHostEnvironment env, ILogger<BackupController> logger)
        {
            _context = context;
            _env = env;
            _logger = logger;
        }

        // pomocná metoda: čeká až bude soubor dostupný pro čtení
        private async Task WaitForFileReady(string path, int maxRetries = 20, int delayMs = 200)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    using (var fs = System.IO.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        return;
                    }
                }
                catch (IOException)
                {
                    await Task.Delay(delayMs);
                }
            }
            throw new IOException($"Soubor {path} je stále uzamčen po {maxRetries} pokusech.");
        }

        // GET: /Backup/Export
        // stáhne zip s kopií DB a obsahem wwwroot/uploads
        // GET: /Backup/Export
        [HttpGet]
        public async Task<IActionResult> Export()
        {
            var dbConn = _context.Database.GetDbConnection();
            var connString = dbConn?.ConnectionString;
            var zipName = $"backup_{DateTime.UtcNow:yyyyMMddHHmmss}.zip";

            // pokusíme se zjistit skutečnou cestu DB souboru ze connection stringu
            var dataSource = GetSqliteDataSource(connString);
            // pokud nenajdeme, fallback na projektový photoapp.db
            if (string.IsNullOrEmpty(dataSource))
            {
                dataSource = Path.Combine(_env.ContentRootPath ?? Directory.GetCurrentDirectory(), _defaultDbFileName);
            }

            if (!System.IO.File.Exists(dataSource))
            {
                return StatusCode(500, $"Nelze najít DB soubor: {dataSource}");
            }

            // helper: zkusit otevřít soubor pro čtení se sdílením ReadWrite (retry)
            async Task<bool> TryOpenForRead(string path, int retries = 40, int delayMs = 200)
            {
                for (int i = 0; i < retries; i++)
                {
                    try
                    {
                        using (var fs = System.IO.File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            return true;
                        }
                    }
                    catch (IOException)
                    {
                        await Task.Delay(delayMs);
                    }
                }
                return false;
            }

            var uploadsFolder = Path.Combine(_env.WebRootPath ?? "", "uploads");

            // 1) Nejprve zkusíme přímé přidání aktuálního DB souboru do ZIP (nejjednodušší)
            try
            {
                var ready = await TryOpenForRead(dataSource, retries: 60, delayMs: 200);
                if (!ready)
                {
                    _logger.LogWarning("Export: nelze otevřít zdrojovou DB pro čtení (sdílení). Pokusím se o BackupDatabase fallback.");
                }
                else
                {
                    using (var ms = new MemoryStream())
                    {
                        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        {
                            // přidat DB přímo z dataSource - otevřeme s FileShare.ReadWrite
                            var dbEntry = zip.CreateEntry("database.db", CompressionLevel.Optimal);
                            using (var zs = dbEntry.Open())
                            using (var fs = System.IO.File.Open(dataSource, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            {
                                await fs.CopyToAsync(zs);
                            }

                            // přidat uploads
                            if (Directory.Exists(uploadsFolder))
                            {
                                var files = Directory.GetFiles(uploadsFolder, "*", SearchOption.AllDirectories);
                                foreach (var file in files)
                                {
                                    var relPath = Path.GetRelativePath(uploadsFolder, file).Replace('\\', '/');
                                    var entryPath = Path.Combine("uploads", relPath).Replace('\\', '/');
                                    var entry = zip.CreateEntry(entryPath, CompressionLevel.Optimal);

                                    // pokusíme se otevřít i soubory uploads se sdílením
                                    var readyFile = await TryOpenForRead(file, retries: 30, delayMs: 150);
                                    if (!readyFile)
                                    {
                                        _logger.LogWarning("Export: soubor {0} je zamčen, přeskočím ho.", file);
                                        continue;
                                    }

                                    using (var zs = entry.Open())
                                    using (var fs = System.IO.File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                        await fs.CopyToAsync(zs);
                                }
                            }
                        }

                        ms.Position = 0;
                        var bytes = ms.ToArray();
                        return File(bytes, "application/zip", zipName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Export: přímé čtení dataSource selhalo, zkusím fallback BackupDatabase.");
            }

            // 2) Fallback: pokusíme se udělat konzistentní snapshot přes BackupDatabase -> temp file -> zip
            string tmpDbFile = Path.Combine(Path.GetTempPath(), $"photoapp_backup_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}.db");
            bool createdTmp = false;
            try
            {
                if (!string.IsNullOrEmpty(connString) && connString.ToLowerInvariant().Contains("data source"))
                {
                    try
                    {
                        using (var source = new SqliteConnection(connString))
                        {
                            await source.OpenAsync();
                            using (var dest = new SqliteConnection($"Data Source={tmpDbFile}"))
                            {
                                await dest.OpenAsync();
                                source.BackupDatabase(dest);
                            }
                        }
                        createdTmp = System.IO.File.Exists(tmpDbFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BackupDatabase selhalo.");
                        try { if (System.IO.File.Exists(tmpDbFile)) System.IO.File.Delete(tmpDbFile); } catch { }
                    }
                }

                if (!createdTmp)
                {
                    // poslední fallback: přímé zkopírování dataSource -> tmp
                    try
                    {
                        System.IO.File.Copy(dataSource, tmpDbFile);
                        createdTmp = System.IO.File.Exists(tmpDbFile);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Fallback copy DB selhalo.");
                    }
                }

                if (!createdTmp)
                {
                    return StatusCode(500, "Nelze vytvořit dočasnou kopii DB pro zálohu (všechny pokusy selhaly). Zkuste zavřít DB nástroje, vypnout antivirus nebo restartovat aplikaci.");
                }

                // čekejme než bude tmp soubor čitelný
                var readyTmp = await TryOpenForRead(tmpDbFile, retries: 60, delayMs: 200);
                if (!readyTmp)
                {
                    return StatusCode(500, $"Soubor {tmpDbFile} je stále uzamčen po opakovaných pokusech.");
                }

                using (var ms = new MemoryStream())
                {
                    using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
                    {
                        var entry = zip.CreateEntry("database.db", CompressionLevel.Optimal);
                        using (var zs = entry.Open())
                        using (var fs = System.IO.File.Open(tmpDbFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                            await fs.CopyToAsync(zs);

                        if (Directory.Exists(uploadsFolder))
                        {
                            var files = Directory.GetFiles(uploadsFolder, "*", SearchOption.AllDirectories);
                            foreach (var file in files)
                            {
                                var relPath = Path.GetRelativePath(uploadsFolder, file).Replace('\\', '/');
                                var entryPath = Path.Combine("uploads", relPath).Replace('\\', '/');
                                var e = zip.CreateEntry(entryPath, CompressionLevel.Optimal);

                                var readyFile = await TryOpenForRead(file, retries: 30, delayMs: 150);
                                if (!readyFile)
                                {
                                    _logger.LogWarning("Export: soubor {0} je zamčen, přeskočím ho.", file);
                                    continue;
                                }

                                using (var zs = e.Open())
                                using (var fs = System.IO.File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                                    await fs.CopyToAsync(zs);
                            }
                        }
                    }

                    ms.Position = 0;
                    var bytes = ms.ToArray();
                    try { if (System.IO.File.Exists(tmpDbFile)) System.IO.File.Delete(tmpDbFile); } catch { }
                    return File(bytes, "application/zip", zipName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Export: chyba při fallback exportu.");
                try { if (System.IO.File.Exists(tmpDbFile)) System.IO.File.Delete(tmpDbFile); } catch { }
                return StatusCode(500, "Chyba při vytváření zálohy: " + ex.Message);
            }
        }
        // POST: /Backup/Restore
        // přijme ZIP se strukturou (database.db a uploads/*) a obnoví DB + uploads
        // Upozornění: tento endpoint přepíše databázový soubor a soubory uploads! Používejte opatrně.
        [HttpPost]
        [RequestSizeLimit(200 * 1024 * 1024)] // např. limit 200 MB — upravte dle potřeby

        public async Task<IActionResult> Restore(IFormFile backupZip)
        {
            if (backupZip == null || backupZip.Length == 0)
                return BadRequest("Soubor nebyl nahrán.");

            var tmpFolder = Path.Combine(Path.GetTempPath(), $"photoapp_import_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tmpFolder);

            var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
            var dbPath = Path.Combine(_env.ContentRootPath ?? Directory.GetCurrentDirectory(), _defaultDbFileName);

            try
            {
                // uložit nahraný ZIP do dočasné složky
                var zipPath = Path.Combine(tmpFolder, "backup.zip");
                using (var stream = System.IO.File.Create(zipPath))
                    await backupZip.CopyToAsync(stream);

                // rozbalit ZIP
                ZipFile.ExtractToDirectory(zipPath, tmpFolder, true);

                // nahradit databázi
                var backupDb = Path.Combine(tmpFolder, "database.db");
                if (System.IO.File.Exists(backupDb))
                {
                    await _context.Database.CloseConnectionAsync(); // zavřít spojení
                    System.IO.File.Copy(backupDb, dbPath, overwrite: true);
                }
                else
                    return BadRequest("V ZIPu nebyl nalezen soubor database.db.");

                // nahradit složku uploads
                var backupUploads = Path.Combine(tmpFolder, "uploads");
                if (Directory.Exists(backupUploads))
                {
                    if (Directory.Exists(uploadsFolder))
                        Directory.Delete(uploadsFolder, true);

                    Directory.CreateDirectory(uploadsFolder);
                    CopyDirectory(backupUploads, uploadsFolder);
                }

                return Ok(new { message = "Obnova úspěšná." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Chyba při obnově zálohy.");
                return StatusCode(500, "Chyba při obnově: " + ex.Message);
            }
            finally
            {
                try { Directory.Delete(tmpFolder, true); } catch { }
            }
        }

        // pomocná metoda na kopírování složek
        private void CopyDirectory(string sourceDir, string targetDir)
        {
            foreach (var dir in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(sourceDir, targetDir));

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
                System.IO.File.Copy(file, file.Replace(sourceDir, targetDir), true);
        }

        // pomocná metoda pro parsování Data Source z connection stringu SQLite
        private string GetSqliteDataSource(string connString)
        {
            if (string.IsNullOrWhiteSpace(connString)) return null;
            // jednoduché parsování: najdeme "Data Source=" nebo "DataSource="
            var lower = connString.ToLowerInvariant();
            var key = "data source=";
            var idx = lower.IndexOf(key);
            if (idx < 0) { key = "datasource="; idx = lower.IndexOf(key); }
            if (idx < 0) return null;
            var start = idx + key.Length;
            var rest = connString.Substring(start).Trim();
            // connection string může obsahovat ; oddělovač
            var endIdx = rest.IndexOf(';');
            var path = endIdx >= 0 ? rest.Substring(0, endIdx) : rest;
            // odstranění uvozovek pokud jsou
            path = path.Trim().Trim('"').Trim('\'');
            // pokud relativní cesta, přepočítat na absolutní (relativní k current dir)
            if (!Path.IsPathRooted(path))
            {
                path = Path.GetFullPath(Path.Combine(_env.ContentRootPath ?? Directory.GetCurrentDirectory(), path));
            }
            return path;
        }
    }
}