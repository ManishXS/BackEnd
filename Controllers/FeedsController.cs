using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using BackEnd.Entities;
using BackEnd.Models;
using BackEnd.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;

namespace BackEnd.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FeedsController : ControllerBase
    {
        private readonly CosmosDbContext _dbContext;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ServiceBusSender _serviceBusSender;
        private readonly string _feedContainer = "media";  // Blob container for storing feeds
        private static readonly string _cdnBaseUrl = "https://tenxcdn.azureedge.net/media/";
        //private static readonly string _cdnBaseUrl = "https://storagetenx.blob.core.windows.net/media/";

        public FeedsController(CosmosDbContext dbContext, BlobServiceClient blobServiceClient, ServiceBusClient serviceBusClient)
        {
            _dbContext = dbContext;
            _blobServiceClient = blobServiceClient;
            _serviceBusSender = serviceBusClient.CreateSender("new-feed-notifications");
        }

        /// <summary>
        /// Upload a new feed with media.
        /// </summary>
        [HttpPost("uploadFeed")]
        public async Task<IActionResult> UploadFeed([FromForm] FeedUploadModel model)
        {
            try
            {
                
                // Ensure required fields are present
                if (model.File == null || string.IsNullOrEmpty(model.UserId) || string.IsNullOrEmpty(model.FileName))
                {
                    return BadRequest("Missing required fields.");
                }

                // Get Blob container reference
                var containerClient = _blobServiceClient.GetBlobContainerClient(_feedContainer);

                // Generate a unique Blob name using a unique value
                var blobName = $"{Guid.NewGuid()}-{model.File.FileName}";
                var blobClient = containerClient.GetBlobClient(blobName);

                // Upload the file to Blob Storage
                using (var stream = model.File.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream);
                }

                // Get the Blob URL
                var blobUrl = blobClient.Uri.ToString();

                // Save the feed data to CosmosDB
                //var feed = new Feed
                //{
                //    id = Guid.NewGuid().ToString(),  // Generate unique ID for the feed
                //    UserId = model.UserId,
                //    Description = model.Description,
                //    FeedUrl = blobUrl,  // Set the Blob URL
                //    UploadDate = DateTime.UtcNow
                //};
                //await _dbContext.PostsContainer.UpsertItemAsync(feed);


                var userPost = new UserPost
                {
                    PostId = Guid.NewGuid().ToString(),
                    Title = model.ProfilePic,
                    Content = _cdnBaseUrl+""+ blobName,
                    Caption= model.Caption,
                    AuthorId = model.UserId,
                    AuthorUsername = model.UserName,
                    DateCreated = DateTime.UtcNow,
                };

                //Insert the new blog post into the database.
                await _dbContext.PostsContainer.UpsertItemAsync<UserPost>(userPost, new PartitionKey(userPost.PostId));


                return Ok(new { Message = "Feed uploaded successfully.", FeedId = userPost.PostId });
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
                //var queryOptions = new QueryRequestOptions { MaxItemCount = pageSize };

                // Start with the base query
                //IQueryable<Feed> query = _dbContext.FeedsContainer
                //    .GetItemLinqQueryable<Feed>(requestOptions: queryOptions);

                // Apply the userId filter if provided
                //if (!string.IsNullOrEmpty(userId))
                //{
                //    query = query.Where(feed => feed.UserId == userId);
                //}

                // Apply ordering after filtering
                //var orderedQuery = query.OrderByDescending(feed => feed.UploadDate) as IOrderedQueryable<Feed>;

                // Execute the query with pagination using continuation tokens
                //var iterator = orderedQuery.ToFeedIterator();
                //var feeds = new List<Feed>();

                //while (iterator.HasMoreResults)
                //{
                //    var response = await iterator.ReadNextAsync();
                //    feeds.AddRange(response);

                //    // Optionally, handle the continuation token for pagination
                //    var continuationToken = response.ContinuationToken;
                //}

                //foreach (var item in feeds)
                //{
                //    item.ProfilePic = "https://storagetenx.blob.core.windows.net/profilepic/" + Utility.GetLastPartAfterHyphen(item.FeedUrl);
                //}



                var m = new BlogHomePageViewModel();
                var numberOfPosts = 100;
                var userPosts = new List<UserPost>();

                var queryString = $"SELECT TOP {numberOfPosts} * FROM f WHERE f.type='post' ORDER BY f.dateCreated DESC";
                var query = _dbContext.FeedsContainer.GetItemQueryIterator<UserPost>(new QueryDefinition(queryString));
                while (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();
                    var ru = response.RequestCharge;
                    userPosts.AddRange(response.ToList());
                }

                //if there are no posts in the feedcontainer, go to the posts container.
                // There may be one that has not propagated to the feed container yet by the azure function (or the azure function is not running).
                if (!userPosts.Any())
                {
                    var queryFromPostsContainter = _dbContext.PostsContainer.GetItemQueryIterator<UserPost>(new QueryDefinition(queryString));
                    while (queryFromPostsContainter.HasMoreResults)
                    {
                        var response = await queryFromPostsContainter.ReadNextAsync();
                        var ru = response.RequestCharge;
                        userPosts.AddRange(response.ToList());
                    }
                }

                m.BlogPostsMostRecent = userPosts;

                return Ok(m);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error retrieving feeds: {ex.Message}");
            }
        }
    }
}
