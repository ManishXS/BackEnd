using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Threading.Tasks;

namespace BackEnd.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MediaController : ControllerBase
    {
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _feedContainer = "media"; // Ensure this matches your container name

        public MediaController(BlobServiceClient blobServiceClient)
        {
            _blobServiceClient = blobServiceClient;
        }

        [HttpGet("stream/{blobName}")]
        public async Task<IActionResult> StreamMedia(string blobName, long startByte = 0, long endByte = -1)
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_feedContainer);
            var blobClient = containerClient.GetBlobClient(blobName);

            if (!await blobClient.ExistsAsync())
            {
                return NotFound("Media not found.");
            }

            var blobProperties = await blobClient.GetPropertiesAsync();
            var fileSize = blobProperties.Value.ContentLength;

            // Adjust the end byte if it’s beyond the file size or unspecified
            if (endByte == -1 || endByte > fileSize)
            {
                endByte = fileSize - 1;
            }

            // Use BlobOpenReadOptions with the specified range
            var options = new BlobOpenReadOptions(allowModifications: false)
            {
                Position = startByte
            };

            // Open the blob stream from the specified position
            var stream = await blobClient.OpenReadAsync(options);

            // Retrieve and set the correct content type based on the file extension
            string contentType = GetContentType(blobName);
            Response.ContentType = contentType;

            // Set content length based on the range
            Response.ContentLength = endByte - startByte + 1;

            // Return the stream as the response
            return File(stream, contentType);
        }

        private string GetContentType(string fileName)
        {
            var fileExtension = Path.GetExtension(fileName).ToLowerInvariant();
            return fileExtension switch
            {
                ".mp4" => "video/mp4",
                ".mov" => "video/quicktime",
                ".avi" => "video/x-msvideo",
                ".mkv" => "video/x-matroska",
                ".webm" => "video/webm",
                ".flv" => "video/x-flv",
                ".ogg" => "video/ogg",
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                _ => "application/octet-stream"
            };
        }
    }
}
