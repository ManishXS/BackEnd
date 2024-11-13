using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
                if (model.File == null || string.IsNullOrEmpty(model.UserId) || string.IsNullOrEmpty(model.FileName))
                {
                    return BadRequest("Missing required fields.");
                }

                var containerClient = _blobServiceClient.GetBlobContainerClient(_feedContainer);
                var blobName = $"{Guid.NewGuid()}-{model.File.FileName}";
                var blobClient = containerClient.GetBlobClient(blobName);

                // Upload the file to blob storage
                using (var stream = model.File.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream);
                }

                var blobUrl = blobClient.Uri.ToString();

                // Create the Feed object to store in Cosmos DB
                var feed = new Feed
                {
                    id = Guid.NewGuid().ToString(),
                    UserId = model.UserId,
                    Description = model.Description,
                    FeedUrl = blobUrl,
                    UploadDate = DateTime.UtcNow
                };

                // Store the feed document in Cosmos DB
                await _dbContext.FeedsContainer.CreateItemAsync(feed);

                // Return the blob URL and feed document in the response
                return Ok(new
                {
                    Message = "Feed uploaded successfully.",
                    FeedId = feed.id,
                    FeedUrl = blobUrl,
                    FeedDocument = feed // You can return the entire feed document, or just select necessary properties.
                });
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
