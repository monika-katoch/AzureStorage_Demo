using blob_demo.Models;
using blob_demo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace blob_demo.Controllers;

public class FilesController : Controller
{
    private readonly IBlobStorageService _blobs;
    private readonly BlobStorageOptions _options;

    public FilesController(IBlobStorageService blobs, IOptions<BlobStorageOptions> options)
    {
        _blobs = blobs;
        _options = options.Value;
    }

    // GET / and /Files — the asset browser (FR-1.2).
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var model = new FileListViewModel
        {
            ContainerName = _options.ContainerName,
            MaxFileSizeBytes = _options.MaxFileSizeBytes,
            Files = await _blobs.ListFilesAsync(ct)
        };
        return View(model);
    }

    // POST /Files/Upload (FR-2).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(60_000_000)] // slightly above the 50 MB app limit so we can return a friendly error
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null)
        {
            TempData["Error"] = "Please choose a file to upload.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _blobs.UploadAsync(file, ct);
        if (result.Success)
            TempData["Success"] = result.Message;
        else
            TempData["Error"] = result.Message;

        return RedirectToAction(nameof(Index));
    }

    // GET /Files/Download/{blobName} — streams the blob back (FR-3.1).
    [HttpGet]
    public async Task<IActionResult> Download(string blobName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(blobName))
            return BadRequest();

        var download = await _blobs.DownloadAsync(blobName, ct);
        if (download is null)
            return NotFound();

        return new FileStreamResult(download.Content, download.ContentType)
        {
            FileDownloadName = download.FileName
        };
    }

    // GET /Files/SasLink/{blobName} — returns a short-lived SAS URL (FR-3.2).
    [HttpGet]
    public IActionResult SasLink(string blobName)
    {
        if (string.IsNullOrWhiteSpace(blobName))
            return BadRequest();

        var uri = _blobs.GenerateReadSasUri(blobName);
        if (uri is null)
        {
            TempData["Error"] = "Could not generate a SAS link for this blob.";
            return RedirectToAction(nameof(Index));
        }

        TempData["SasLink"] = uri.ToString();
        TempData["SasExpiry"] = $"{_options.SasExpiryMinutes} minutes";
        return RedirectToAction(nameof(Index));
    }
}
