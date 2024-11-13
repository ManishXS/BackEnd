using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BackEnd.Entities;
using BackEnd.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

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
                // Check for required fields
                if (model.File == null || string.IsNullOrEmpty(model.UserId) || string.IsNullOrEmpty(model.FileName))
                {
                    return BadRequest("File, UserId, and FileName are required.");
                }

                // Get blob container client and set the unique blob name
                var containerClient = _blobServiceClient.GetBlobContainerClient(_feedContainer);
                var blobName = $"{Guid.NewGuid()}-{Path.GetFileName(model.File.FileName)}";
                var blobClient = containerClient.GetBlobClient(blobName);

                // Upload file to blob storage with its content type
                using (var stream = model.File.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = model.ContentType });
                }

                // Get the full URL of the uploaded blob
                var blobUrl = blobClient.Uri.ToString();
                Console.WriteLine($"Blob uploaded with URL: {blobUrl}");

                // Create feed object for Cosmos DB
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

                // Insert feed into Cosmos DB
                await _dbContext.FeedsContainer.CreateItemAsync(feed);
                Console.WriteLine($"Document stored in Cosmos DB with id: {feed.id}");

                // Return blob URL and feed document in response
                return Ok(new
                {
                    Message = "Feed uploaded successfully.",
                    FeedId = feed.id,
                    FeedUrl = blobUrl,
                    FeedDocument = feed
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error uploading feed: {ex.Message}");
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
