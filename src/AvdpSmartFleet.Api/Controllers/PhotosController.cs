using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AvdpSmartFleet.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/photos")]
public class PhotosController : ControllerBase
{
    private readonly IWebHostEnvironment _env;
    private static readonly string[] AllowedTypes = { "image/jpeg", "image/png", "image/webp" };
    private const long MaxBytes = 5 * 1024 * 1024;

    public PhotosController(IWebHostEnvironment env) => _env = env;

    [HttpPost("upload")]
    [RequestSizeLimit(MaxBytes)]
    public async Task<ActionResult<object>> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "No file" });
        if (file.Length > MaxBytes) return BadRequest(new { error = "File too large (max 5 MB)" });
        if (!AllowedTypes.Contains(file.ContentType)) return BadRequest(new { error = "Unsupported file type" });

        var photosDir = Path.Combine(_env.ContentRootPath, "uploads", "photos");
        Directory.CreateDirectory(photosDir);

        var ext = file.ContentType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/webp" => ".webp",
            _ => ".bin"
        };
        var fileName = $"{DateTime.UtcNow:yyyyMMdd}_{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(photosDir, fileName);
        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        var url = $"/uploads/photos/{fileName}";
        return Ok(new { url, size = file.Length });
    }
}
