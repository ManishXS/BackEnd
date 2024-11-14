using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using BlogWebApp.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using BackEnd.Entities;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;

namespace BackEnd.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly CosmosDbContext _dbContext;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ServiceBusSender _serviceBusSender;
        private readonly string _feedContainer = "media";  // Blob container for storing feeds

        public UserController(CosmosDbContext dbContext, BlobServiceClient blobServiceClient, ServiceBusClient serviceBusClient)
        {
            _dbContext = dbContext;
            _blobServiceClient = blobServiceClient;
            _serviceBusSender = serviceBusClient.CreateSender("new-feed-notifications");
        }


        [Route("user")]
        [HttpPost]
        public async Task<IActionResult> UserProfile(string newUsername)
        {

            var oldUsername = User.Identity.Name;

            if (newUsername != oldUsername)
            {
              
                var queryDefinition = new QueryDefinition("SELECT * FROM u WHERE u.type = 'user' AND u.username = @username").WithParameter("@username", oldUsername);

                var query = _dbContext.UsersContainer.GetItemQueryIterator<BlogUser>(queryDefinition);

                List<BlogUser> results = new List<BlogUser>();
                while (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();

                    results.AddRange(response.ToList());
                }

                if (results.Count > 1)
                {
                    throw new Exception($"More than one user found for username '{newUsername}'");
                }

                var u = results.SingleOrDefault();


                //set the new username on the user object.
                u.Username = newUsername;

                //first try to create the username in the partition with partitionKey "unique_username" to confirm the username does not exist already
                var uniqueUsername = new UniqueUsername { Username = u.Username };
                await _dbContext.UsersContainer.CreateItemAsync<UniqueUsername>(uniqueUsername, new PartitionKey(uniqueUsername.UserId));

                u.Action = "Update";

                //if we get past adding a new username for partition key "unique_username", then go ahead and update this user's username
                await _dbContext.UsersContainer.ReplaceItemAsync<BlogUser>(u, u.UserId, new PartitionKey(u.UserId));

                //then we need to delete the old "unique_username" for the username that just changed.
                var queryDefinition1 = new QueryDefinition("SELECT * FROM u WHERE u.userId = 'unique_username' AND u.type = 'unique_username' AND u.username = @username").WithParameter("@username", oldUsername);
                var query1 = _dbContext.UsersContainer.GetItemQueryIterator<BlogUniqueUsername>(queryDefinition1);
                while (query1.HasMoreResults)
                {
                    var response = await query1.ReadNextAsync();

                    var oldUniqueUsernames = response.ToList();

                    foreach (var oldUniqueUsername in oldUniqueUsernames)
                    {
                        //Last delete the old unique username entry
                        await _dbContext.UsersContainer.DeleteItemAsync<BlogUser>(oldUniqueUsername.Id, new PartitionKey("unique_username"));
                    }
                }
            }

            var m = new UserProfileViewModel
            {
                OldUsername = newUsername,
                NewUsername = newUsername
            };

            return Ok(m);
        }


        [Route("user/{userId}/posts")]
        [HttpGet]
        public async Task<IActionResult> UserPosts(string userId)
        {
            
            var blogPosts = new List<UserPost>();
            var queryString = $"SELECT * FROM p WHERE p.type='post' AND p.userId = @UserId ORDER BY p.dateCreated DESC";
            var queryDef = new QueryDefinition(queryString);
            queryDef.WithParameter("@UserId", userId);
            var query = _dbContext.UsersContainer.GetItemQueryIterator<UserPost>(queryDef);

            while (query.HasMoreResults)
            {
                var response = await query.ReadNextAsync();
                var ru = response.RequestCharge;
                blogPosts.AddRange(response.ToList());
            }

            var username = "";

            var firstPost = blogPosts.FirstOrDefault();
            if (firstPost != null)
            {
                username = firstPost.AuthorUsername;
            }

            var m = new UserPostsViewModel
            {
                Username = username,
                Posts = blogPosts
            };
            return Ok(m);
        }
    }
}