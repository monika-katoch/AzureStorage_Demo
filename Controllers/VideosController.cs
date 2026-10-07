using blob_demo.Models;
using blob_demo.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace blob_demo.Controllers;

public class VideosController : Controller
{
    private readonly IVideoStorageService _videos;
    private readonly VideoStorageOptions _options;

    public VideosController(IVideoStorageService videos, IOptions<VideoStorageOptions> options)
    {
        _videos = videos;
        _options = options.Value;
    }

    // GET /Videos?play={blobName} — gallery + player.
    [HttpGet]
    public async Task<IActionResult> Index(string? play, CancellationToken ct)
    {
        var model = new VideoPageViewModel
        {
            ContainerName = _options.ContainerName,
            MaxFileSizeBytes = _options.MaxFileSizeBytes,
            Videos = await _videos.ListAsync(ct),
            NowPlaying = play
        };
        return View(model);
    }

    // POST /Videos/Upload — large multipart upload (limit raised in Program.cs).
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(2_147_483_648)] // 2 GB
    public async Task<IActionResult> Upload(IFormFile? file, CancellationToken ct)
    {
        if (file is null)
        {
            TempData["Error"] = "Please choose a video to upload.";
            return RedirectToAction(nameof(Index));
        }

        var result = await _videos.UploadAsync(file, ct);
        if (result.Success)
        {
            TempData["Success"] = result.Message;
            return RedirectToAction(nameof(Index), new { play = result.BlobName });
        }

        TempData["Error"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    // GET /Videos/Stream?blobName=... — THE streaming endpoint.
    // enableRangeProcessing:true makes ASP.NET Core honor the browser's Range header,
    // reply 206 Partial Content with Content-Range, and serve only the requested
    // bytes from the seekable blob stream. That is what lets <video> seek instantly
    // and start playback before the whole file has transferred.
    [HttpGet]
    public async Task<IActionResult> Stream(string blobName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(blobName))
            return BadRequest();

        var video = await _videos.OpenReadAsync(blobName, ct);
        if (video is null)
            return NotFound();

        return File(video.Content, video.ContentType, enableRangeProcessing: true);
    }
}
