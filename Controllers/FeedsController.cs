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

                // Unique blob name with GUID for the file
                var blobName = $"{Guid.NewGuid()}-{model.FileName}";
                var blobClient = containerClient.GetBlobClient(blobName);

                // Accessing the blob as a BlockBlobClient for chunked upload
                var blockBlobClient = containerClient.GetBlockBlobClient(blobName); // Fix: pass blobName

                List<string> blockIds = new List<string>();  // List to track the block IDs

                // Upload the chunk to the blob storage
                using (var stream = model.File.OpenReadStream())
                {
                    // Generate a GUID for the chunk block
                    string blockId = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                    blockIds.Add(blockId);  // Add the block ID to the list

                    // Upload the block (chunk) to the blob storage
                    await blockBlobClient.StageBlockAsync(blockId, stream);
                }

                // If the chunk is the last one, commit all blocks to form the complete file
                if (model.ChunkIndex == model.TotalChunks - 1)
                {
                    // Commit all blocks (chunks) to form the final file
                    await blockBlobClient.CommitBlockListAsync(blockIds);
                }

                return Ok(new { Message = "Feed uploaded successfully." });
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
