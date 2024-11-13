﻿using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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

        // Static dictionary to store current session filenames and counters
        private static Dictionary<string, int> _fileNameCounters = new Dictionary<string, int>();

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

                // Generate a unique blob name based on the file name and existing files in the container
                var blobName = await GenerateUniqueBlobName(containerClient, model.File.FileName);
                var blobClient = containerClient.GetBlobClient(blobName);

                // Upload the file to blob storage
                using (var stream = model.File.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = model.ContentType });
                }

                var blobUrl = blobClient.Uri.ToString();
                Console.WriteLine($"Blob uploaded with URL: {blobUrl}");

                // Create the Feed object to store in Cosmos DB
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

                // Store the feed document in Cosmos DB
                await _dbContext.FeedsContainer.CreateItemAsync(feed);
                Console.WriteLine($"Document stored in Cosmos DB with id: {feed.id}");

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

        // Method to generate a unique blob name by checking existing files
        private async Task<string> GenerateUniqueBlobName(BlobContainerClient containerClient, string originalFileName)
        {
            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(originalFileName);
            var fileExtension = Path.GetExtension(originalFileName);
            var blobName = originalFileName;

            if (_fileNameCounters.ContainsKey(fileNameWithoutExtension))
            {
                // If the file name already exists in our dictionary, increment the counter
                int counter = ++_fileNameCounters[fileNameWithoutExtension];
                blobName = $"{fileNameWithoutExtension}-{counter}{fileExtension}";
            }
            else
            {
                // Check if a blob with the same name already exists
                var existingBlob = containerClient.GetBlobClient(blobName);
                if (await existingBlob.ExistsAsync())
                {
                    // If it exists, initialize a counter for this file name
                    int counter = 1;
                    blobName = $"{fileNameWithoutExtension}-{counter}{fileExtension}";
                    _fileNameCounters[fileNameWithoutExtension] = counter;
                }
                else
                {
                    // No duplicates; add the filename without appending a counter
                    _fileNameCounters[fileNameWithoutExtension] = 0;
                }
            }

            return blobName;
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
