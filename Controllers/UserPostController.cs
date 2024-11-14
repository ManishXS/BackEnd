﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using BackEnd.ViewModels;
using BackEnd.Entities;
using Azure.Messaging.ServiceBus;
using Azure.Storage.Blobs;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Hosting;
using BackEnd.Models;

namespace BackEnd.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserPostController : ControllerBase
    {
        private readonly CosmosDbContext _dbContext;
        private readonly BlobServiceClient _blobServiceClient;
        private readonly ServiceBusSender _serviceBusSender;
        private readonly string _feedContainer = "media";  // Blob container for storing feeds

        public UserPostController(CosmosDbContext dbContext, BlobServiceClient blobServiceClient, ServiceBusClient serviceBusClient)
        {
            _dbContext = dbContext;
            _blobServiceClient = blobServiceClient;
            _serviceBusSender = serviceBusClient.CreateSender("new-feed-notifications");
        }


        [Route("post/{postId}")]
        [HttpGet]
        public async Task<IActionResult> PostView(string postId, string userId)
        {

            //When getting the blogpost from the Posts container, the id is postId and the partitionKey is also postId.
            //  This will automatically return only the type="post" for this postId (and not the type=comment or any other types in the same partition postId)
            ItemResponse<UserPost> response = await _dbContext.PostsContainer.ReadItemAsync<UserPost>(postId, new PartitionKey(postId));
            var bp = response.Resource;



            var queryString1 = $"SELECT * FROM p WHERE p.type='comment' AND p.postId = @PostId ORDER BY p.dateCreated DESC";
            var queryDef1 = new QueryDefinition(queryString1);
            queryDef1.WithParameter("@PostId", postId);
            var query1 = _dbContext.PostsContainer.GetItemQueryIterator<UserPostComment>(queryDef1);

            List<UserPostComment> comments = new List<UserPostComment>();
            while (query1.HasMoreResults)
            {
                var resp1 = await query1.ReadNextAsync();
                var ru = resp1.RequestCharge;
                comments.AddRange(resp1.ToList());
            }
            var userLikedPost = false;




            var queryString2 = $"SELECT TOP 1 * FROM p WHERE p.type='like' AND p.postId = @PostId AND p.userId = @UserId ORDER BY p.dateCreated DESC";

            var queryDef2 = new QueryDefinition(queryString2);
            queryDef2.WithParameter("@PostId", postId);
            queryDef2.WithParameter("@UserId", userId);
            var query2 = _dbContext.PostsContainer.GetItemQueryIterator<UserPostLike>(queryDef2);

            UserPostLike like = null;
            if (query2.HasMoreResults)
            {
                var resp2 = await query2.ReadNextAsync();
                var ru = resp2.RequestCharge;
                like = resp2.FirstOrDefault();
            }
            userLikedPost = like != null;

            var m = new BlogPostViewViewModel
            {
                PostId = bp.PostId,
                Title = bp.Title,
                Content = bp.Content,
                CommentCount = bp.CommentCount,
                Comments = comments,
                UserLikedPost = userLikedPost,
                LikeCount = bp.LikeCount,
                AuthorId = bp.AuthorId,
                AuthorUsername = bp.AuthorUsername
            };
            return Ok(m);
        }


        [Route("post/edit/{postId}")]
        [HttpGet]
        public async Task<IActionResult> PostEdit(string postId)
        {
            //When getting the blogpost from the Posts container, the id is postId and the partitionKey is also postId.
            //  This will automatically return only the type="post" for this postId (and not the type=comment or any other types in the same partition postId)
            ItemResponse<UserPost> response = await _dbContext.PostsContainer.ReadItemAsync<UserPost>(postId, new PartitionKey(postId));
            var ru = response.RequestCharge;
            var bp = response.Resource;

            var m = new BlogPostEditViewModel
            {
                Title = bp.Title,
                Content = bp.Content
            };
            return Ok(m);
        }


        [Route("post/new")]
        [HttpPost]
        public async Task<IActionResult> PostNew(BlogPostEditViewModel blogPostChanges)
        {
            blogPostChanges.Content = "Testing Text";

            var blogPost = new UserPost
            {
                PostId = Guid.NewGuid().ToString(),
                Title = blogPostChanges.Title,
                Content = blogPostChanges.Content,
                AuthorId = User.Claims.FirstOrDefault(p => p.Type == ClaimTypes.NameIdentifier).Value,
                AuthorUsername = User.Identity.Name,
                DateCreated = DateTime.UtcNow,
            };

            //Insert the new blog post into the database.
            await _dbContext.PostsContainer.UpsertItemAsync<UserPost>(blogPost, new PartitionKey(blogPost.PostId));


            //Show the view with a message that the blog post has been created.

            return Ok(blogPostChanges);
        }


        [Route("post/edit/{postId}")]
        [HttpPost]
        public async Task<IActionResult> PostEdit(string postId, BlogPostEditViewModel blogPostChanges)
        {
            ItemResponse<UserPost> response = await _dbContext.PostsContainer.ReadItemAsync<UserPost>(postId, new PartitionKey(postId));
            var ru = response.RequestCharge;
            var bp = response.Resource;

            bp.Title = blogPostChanges.Title;
            bp.Content = blogPostChanges.Content;

            //Update the database with these changes.
            await _dbContext.PostsContainer.UpsertItemAsync<UserPost>(bp, new PartitionKey(bp.PostId));

            //Show the view with a message that the blog post has been updated.
            return Ok(blogPostChanges);
        }


        [Route("PostCommentNew")]
        [HttpPost]
        public async Task<IActionResult> PostCommentNew([FromForm] CommentPost model)
        {

            if (!string.IsNullOrWhiteSpace(model.CommentContent))
            {
                ItemResponse<UserPost> response = await _dbContext.PostsContainer.ReadItemAsync<UserPost>(model.PostId, new PartitionKey(model.PostId));
                var ru = response.RequestCharge;
                var bp = response.Resource;

                if (bp != null)
                {
                    var blogPostComment = new UserPostComment
                    {
                        CommentId = Guid.NewGuid().ToString(),
                        PostId = model.PostId,
                        CommentContent = model.CommentContent,
                        CommentAuthorId = model.CommentAuthorId,
                        CommentAuthorUsername = model.CommentAuthorUsername,
                        CommentDateCreated = DateTime.UtcNow,
                        UserProfileUrl=model.UserProfileUrl,
                    };

 
                    var obj = new dynamic[] { blogPostComment.PostId, blogPostComment };

                    var result = await _dbContext.PostsContainer.Scripts.ExecuteStoredProcedureAsync<string>("createComment", new PartitionKey(blogPostComment.PostId), obj);
                }
            }

            return Ok(new { postId = model.PostId });
        }


        [Route("postLike")]
        [HttpPost]
        public async Task<IActionResult> PostLike([FromForm] LikePost model)
        {

            ItemResponse<UserPost> response = await _dbContext.PostsContainer.ReadItemAsync<UserPost>(model.PostId, new PartitionKey(model.PostId));
            var ru = response.RequestCharge;
            var bp = response.Resource;

            if (bp != null)
            {
                //Check that this user has not already liked this post
                var queryString = $"SELECT TOP 1 * FROM p WHERE p.type='like' AND p.postId = @PostId AND p.userId = @UserId ORDER BY p.dateCreated DESC";

                var queryDef = new QueryDefinition(queryString);
                queryDef.WithParameter("@PostId", model.PostId);
                queryDef.WithParameter("@UserId", model.LikeAuthorId);
                var query = _dbContext.PostsContainer.GetItemQueryIterator<UserPostLike>(queryDef);

                UserPostLike like = null;
                if (query.HasMoreResults)
                {
                    var response1 = await query.ReadNextAsync();
                    var ru1 = response1.RequestCharge;
                    like = response1.FirstOrDefault();
                }


                if (like == null)
                {
                    var userPostLike = new UserPostLike
                    {
                        LikeId = Guid.NewGuid().ToString(),
                        PostId = model.PostId,

                        LikeAuthorId = model.LikeAuthorId,
                        LikeAuthorUsername = model.LikeAuthorUsername,
                        LikeDateCreated = DateTime.UtcNow,
                        UserProfileUrl = model.UserProfileUrl,
                    };

                    var obj = new dynamic[] { userPostLike.PostId, userPostLike };

                    var result = await _dbContext.PostsContainer.Scripts.ExecuteStoredProcedureAsync<string>("createLike", new PartitionKey(userPostLike.PostId), obj);
                }
            }

            return Ok(new { postId = model.PostId });
        }

        [Route("post/{postId}/unlike")]
        [HttpPost]
        public async Task<IActionResult> PostUnlike(string postId)
        {
            ItemResponse<UserPost> response = await this._dbContext.PostsContainer.ReadItemAsync<UserPost>(postId, new PartitionKey(postId));
            var ru = response.RequestCharge;
            var bp = response.Resource;

            if (bp != null)
            {
                var userId = User.Claims.FirstOrDefault(p => p.Type == ClaimTypes.NameIdentifier).Value;
                var obj = new dynamic[] { postId, userId };
                var result = await _dbContext.PostsContainer.Scripts.ExecuteStoredProcedureAsync<string>("deleteLike", new PartitionKey(postId), obj);
            }

            return Ok(new { postId = postId });
        }


        [Route("PostLikes")]
        [HttpGet]
        public async Task<IActionResult> PostLikes(string postId)
        {

            ItemResponse<UserPost> response_ = await _dbContext.PostsContainer.ReadItemAsync<UserPost>(postId, new PartitionKey(postId));
            var bp = response_.Resource;

            var postLikes = new List<UserPostLike>();
            if (bp != null)
            {
                //Check that this user has not already liked this post
                var queryString = $"SELECT * FROM p WHERE p.type='like' AND p.postId = @PostId  ORDER BY p.dateCreated DESC";

                var queryDef = new QueryDefinition(queryString);
                queryDef.WithParameter("@PostId", postId);
                var query = _dbContext.PostsContainer.GetItemQueryIterator<UserPostLike>(queryDef);

                if (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();
                    var ru = response.RequestCharge;
                    postLikes.AddRange(response.ToList());
                }
            }

            return Ok(postLikes);
        }

        [Route("PostComments")]
        [HttpGet]
        public async Task<IActionResult> PostComments(string postId)
        {

            ItemResponse<UserPost> response_ = await _dbContext.PostsContainer.ReadItemAsync<UserPost>(postId, new PartitionKey(postId));
            var bp = response_.Resource;

            var postComments = new List<UserPostComment>();
            if (bp != null)
            {
                //Check that this user has not already liked this post
                var queryString = $"SELECT * FROM p WHERE p.type='comment' AND p.postId = @PostId  ORDER BY p.dateCreated DESC";

                var queryDef = new QueryDefinition(queryString);
                queryDef.WithParameter("@PostId", postId);
                var query = _dbContext.PostsContainer.GetItemQueryIterator<UserPostComment>(queryDef);

                if (query.HasMoreResults)
                {
                    var response = await query.ReadNextAsync();
                    var ru = response.RequestCharge;
                    postComments.AddRange(response.ToList());
                }
            }

            return Ok(postComments);
        }

    }
}