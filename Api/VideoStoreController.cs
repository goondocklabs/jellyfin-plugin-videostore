using Jellyfin.Plugin.VideoStore.Store;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.VideoStore.Api;

/// <summary>POST /VideoStore/Restock?all=true : restock now (admins only; used by the settings page button).</summary>
[ApiController]
[Route("VideoStore")]
[Authorize(Policy = "RequiresElevation")]
public class VideoStoreController : ControllerBase
{
    private readonly Clerk _clerk;

    public VideoStoreController(Clerk clerk)
    {
        _clerk = clerk;
    }

    [HttpPost("Restock")]
    public async Task<ActionResult<object>> Restock([FromQuery] bool all = true)
    {
        var report = await _clerk.RestockAsync(all, null, HttpContext.RequestAborted).ConfigureAwait(false);
        return Ok(new { report });
    }
}
