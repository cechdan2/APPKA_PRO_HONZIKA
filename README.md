# PhotoApp - minimal ASP.NET Core MVC + PWA skeleton

## Quick start

1. Open the folder `photoapp` in Visual Studio 2022 (or VS Code with C# extension).
2. Restore NuGet packages: `dotnet restore`
3. Create initial EF migration (optional) and update DB:
   - `dotnet tool install --global dotnet-ef` (if not installed)
   - `dotnet ef migrations add Init`
   - `dotnet ef database update`
4. Run the app: `dotnet run`
5. Open https://localhost:5001 (or the URL shown) — the gallery is the default page.

## Notes
- Uploaded images are stored in `wwwroot/uploads`.
- For PWA functionality, serve over HTTPS (localhost with dev cert is fine).
- Replace the placeholder icons in `wwwroot/icons`.
