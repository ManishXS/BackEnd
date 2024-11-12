using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using BackEnd.Entities;
using BackEnd.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;

namespace BackEnd.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeedsController : ControllerBase
    {
        private readonly CosmosDbContext _dbContext;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _feedContainer = "media"; 

        public FeedsController(CosmosDbContext dbContext, BlobServiceClient blobServiceClient)
        {
            _dbContext = dbContext;
            _blobServiceClient = blobServiceClient;
        }

        [HttpPost("uploadFeed")]
        public async Task<IActionResult> UploadFeed([FromForm] FeedUploadModel model)
        {
            try
            {
                // Set the file size limit to 0.5 GB
                const long maxFileSize = 524_288_000;

                if (model.File == null || string.IsNullOrEmpty(model.UserId) || string.IsNullOrEmpty(model.FileName))
                {
                    return BadRequest("Missing required fields.");
                }

                // Check the file size limit
                if (model.File.Length > maxFileSize)
                {
                    return BadRequest("File size exceeds the 0.5 GB limit.");
                }

                var containerClient = _blobServiceClient.GetBlobContainerClient(_feedContainer);
                await containerClient.CreateIfNotExistsAsync();

                var blobName = $"{Guid.NewGuid()}-{model.File.FileName}";
                var blockBlobClient = containerClient.GetBlockBlobClient(blobName);

                using (var stream = model.File.OpenReadStream())
                {
                    // Open a writable stream to the blob for chunked uploads
                    using (var blobStream = await blockBlobClient.OpenWriteAsync(overwrite: true, new Azure.Storage.Blobs.Models.BlockBlobOpenWriteOptions
                    {
                        BufferSize = 4 * 1024 * 1024, // 4 MB buffer for chunk size
                    }))
                    {
                        await stream.CopyToAsync(blobStream); // Write the stream in chunks
                        await blobStream.FlushAsync(); // Ensure all data is flushed
                    }
                }

                var blobUrl = blockBlobClient.Uri.ToString();

                var feed = new Feed
                {
                    id = Guid.NewGuid().ToString(),
                    UserId = model.UserId,
                    Description = model.Description,
                    FeedUrl = blobUrl,
                    UploadDate = DateTime.UtcNow
                };

                await _dbContext.FeedsContainer.CreateItemAsync(feed);

                return Ok(new { Message = "Feed uploaded successfully.", FeedId = feed.id });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error uploading feed: {ex.Message}");
            }
        }



        [HttpGet("getUserFeeds")]
        public async Task<IActionResult> GetUserFeeds(string? userId = null, int pageNumber = 1, int pageSize = 10)
        {
            try
            {
                var queryOptions = new QueryRequestOptions { MaxItemCount = pageSize };

                IQueryable<Feed> query = _dbContext.FeedsContainer
                    .GetItemLinqQueryable<Feed>(requestOptions: queryOptions);

                if (!string.IsNullOrEmpty(userId))
                {
                    query = query.Where(feed => feed.UserId == userId);
                }

                var orderedQuery = query.OrderByDescending(feed => feed.UploadDate) as IOrderedQueryable<Feed>;

                var iterator = orderedQuery.ToFeedIterator();
                var feeds = new List<Feed>();

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync();
                    feeds.AddRange(response);
                    var continuationToken = response.ContinuationToken;
                }


                return Ok(feeds);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving feeds: {ex.Message}");
            }
        }


    }
}
