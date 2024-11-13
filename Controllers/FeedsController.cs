using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using BackEnd.Entities;
using BackEnd.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System.Text;

namespace BackEnd.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeedsController : ControllerBase
    {
        private readonly CosmosDbContext _dbContext;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly string _feedContainer = "media";

        // Static dictionary to store current session filenames and counters
        private static Dictionary<string, int> _fileNameCounters = new Dictionary<string, int>();

        public FeedsController(CosmosDbContext dbContext, BlobServiceClient blobServiceClient)
        {
            _dbContext = dbContext;
            _blobServiceClient = blobServiceClient;
        }

        [HttpPost("uploadFeed")]
        public async Task<IActionResult> UploadFeed([FromForm] FeedUploadModel model, [FromForm] int? chunkIndex = null, [FromForm] int? totalChunks = null)
        {
            try
            {
                if (model.File == null || string.IsNullOrEmpty(model.UserId) || string.IsNullOrEmpty(model.FileName))
                {
                    return BadRequest("Missing required fields.");
                }

                var containerClient = _blobServiceClient.GetBlobContainerClient(_feedContainer);
                var blobName = model.FileName;

                // Use BlockBlobClient directly from the container client
                var blockBlobClient = containerClient.GetBlockBlobClient(blobName);

                // For chunked upload
                if (chunkIndex.HasValue && totalChunks.HasValue)
                {
                    var blockId = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{chunkIndex:D6}"));
                    using (var stream = model.File.OpenReadStream())
                    {
                        await blockBlobClient.StageBlockAsync(blockId, stream);
                    }

                    // Commit blocks after final chunk
                    if (chunkIndex.Value == totalChunks.Value - 1)
                    {
                        var blockList = Enumerable.Range(0, totalChunks.Value)
                            .Select(i => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{i:D6}"))).ToList();
                        await blockBlobClient.CommitBlockListAsync(blockList);
                        return await SaveFeedToCosmosDb(blockBlobClient.Uri.ToString(), model);
                    }
                }
                else
                {
                    // Direct upload for small files
                    using (var stream = model.File.OpenReadStream())
                    {
                        await blockBlobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = model.ContentType });
                    }
                    return await SaveFeedToCosmosDb(blockBlobClient.Uri.ToString(), model);
                }

                return Ok(new { Message = "Chunk uploaded successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error uploading feed: {ex.Message}");
            }
        }
        private async Task<IActionResult> SaveFeedToCosmosDb(string blobUrl, FeedUploadModel model)
        {
            var feed = new Feed
            {
                id = Guid.NewGuid().ToString(),
                UserId = model.UserId,
                Description = model.Description,
                FeedUrl = blobUrl,
                ContentType = model.ContentType,
                FileSize = model.FileSize,
                UploadDate = DateTime.UtcNow
            };

            await _dbContext.FeedsContainer.CreateItemAsync(feed);
            return Ok(new
            {
                Message = "Feed uploaded successfully.",
                FeedId = feed.id,
                FeedUrl = blobUrl,
                FeedDocument = feed
            });
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
                }

                return Ok(feeds);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving feeds: {ex.Message}");
                return StatusCode(500, $"Error retrieving feeds: {ex.Message}");
            }
        }
    }
}
