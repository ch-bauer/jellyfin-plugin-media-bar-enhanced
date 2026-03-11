using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using MediaBrowser.Common.Configuration;
using Microsoft.AspNetCore.Authorization;

namespace Jellyfin.Plugin.MediaBarEnhanced.Api
{
    /// <summary>
    /// Controller for handling custom overlay image uploads and retrieval.
    /// </summary>
    [ApiController]
    [Route("MediaBarEnhanced")]
    public class OverlayImageController : ControllerBase
    {
        private readonly IApplicationPaths _applicationPaths;
        private readonly string _imageDirectory;

        public OverlayImageController(IApplicationPaths applicationPaths)
        {
            _applicationPaths = applicationPaths;
            _imageDirectory = Path.Combine(applicationPaths.PluginConfigurationsPath, "Jellyfin.Plugin.MediaBarEnhanced", "Assets");
        }

        /// <summary>
        /// Uploads a new custom overlay image.
        /// </summary>
        [Authorize(Policy = "RequiresElevation")]
        [HttpPost("OverlayImage")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UploadImage([FromForm] IFormFile file, [FromQuery] string? filename = null)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            // Extract original extension or fallback to .jpg
            string extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrWhiteSpace(extension)) extension = ".jpg";
            
            // Delete any existing file with this prefix before saving the new one
            string prefix = string.IsNullOrWhiteSpace(filename) ? "custom_overlay_image_global" : $"custom_overlay_image_{filename}";

            try
            {
                if (!Directory.Exists(_imageDirectory))
                {
                    Directory.CreateDirectory(_imageDirectory);
                }

                // Remove existing
                var existingFiles = Directory.GetFiles(_imageDirectory, $"{prefix}.*");
                foreach(var extFile in existingFiles)
                {
                    System.IO.File.Delete(extFile);
                }

                string targetFileName = $"{prefix}{extension}";
                string targetPath = Path.Combine(_imageDirectory, targetFileName);

                using (var stream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await file.CopyToAsync(stream).ConfigureAwait(false);
                }

                // Return the GET URL that the frontend can use
                var qs = string.IsNullOrWhiteSpace(filename) ? "" : $"?filename={Uri.EscapeDataString(filename)}&";
                var getUrl = $"/MediaBarEnhanced/OverlayImage{qs}{(qs == "" ? "?" : "")}t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                return Ok(new { url = getUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        /// <summary>
        /// Retrieves the custom overlay image.
        /// </summary>
        [HttpGet("OverlayImage")]
        public IActionResult GetImage([FromQuery] string? filename = null)
        {
            string prefix = string.IsNullOrWhiteSpace(filename) ? "custom_overlay_image_global" : $"custom_overlay_image_{filename}";
            
            if (!Directory.Exists(_imageDirectory))
                return NotFound();

            var existingFiles = Directory.GetFiles(_imageDirectory, $"{prefix}.*");
            if (existingFiles.Length == 0)
                return NotFound();

            string targetPath = existingFiles[0];

            // Read the file and return with appropriate MIME type
            var stream = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            
            string mimeType = "application/octet-stream";
            string ext = Path.GetExtension(targetPath).ToLowerInvariant();
            switch (ext) {
                case ".jpg": case ".jpeg": mimeType = "image/jpeg"; break;
                case ".png": mimeType = "image/png"; break;
                case ".gif": mimeType = "image/gif"; break;
                case ".webp": mimeType = "image/webp"; break;
            }
            return File(stream, mimeType);
        }

        /// <summary>
        /// Deletes a custom overlay image.
        /// </summary>
        [Authorize(Policy = "RequiresElevation")]
        [HttpDelete("OverlayImage")]
        public IActionResult DeleteImage([FromQuery] string? filename = null)
        {
            string prefix = string.IsNullOrWhiteSpace(filename) ? "custom_overlay_image_global" : $"custom_overlay_image_{filename}";
            
            if (Directory.Exists(_imageDirectory))
            {
                var existingFiles = Directory.GetFiles(_imageDirectory, $"{prefix}.*");
                foreach(var file in existingFiles)
                {
                    try
                    {
                        System.IO.File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, $"Error deleting file: {ex.Message}");
                    }
                }
                return Ok();
            }

            return NotFound();
        }

        /// <summary>
        /// Renames a custom overlay image (used when a seasonal section is renamed).
        /// </summary>
        [HttpPut("OverlayImage/Rename")]
        public IActionResult RenameImage([FromQuery] string oldName, [FromQuery] string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
            {
                return BadRequest("Both oldName and newName must be provided.");
            }

            if (!Directory.Exists(_imageDirectory))
                return Ok();

            var oldFiles = Directory.GetFiles(_imageDirectory, $"custom_overlay_image_{oldName}.*");
            if (oldFiles.Length == 0)
                return Ok(); 

            try
            {
                string oldPath = oldFiles[0];
                string extension = Path.GetExtension(oldPath);
                string newPath = Path.Combine(_imageDirectory, $"custom_overlay_image_{newName}{extension}");

                // If a file with the new name already exists, delete it first to avoid conflicts
                var existingNewFiles = Directory.GetFiles(_imageDirectory, $"custom_overlay_image_{newName}.*");
                foreach(var existing in existingNewFiles) {
                    System.IO.File.Delete(existing);
                }
                
                System.IO.File.Move(oldPath, newPath);
                
                var qs = $"?filename={Uri.EscapeDataString(newName)}&";
                var getUrl = $"/MediaBarEnhanced/OverlayImage{qs}t={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
                return Ok(new { url = getUrl });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error renaming file: {ex.Message}");
            }
        }
    }
}
