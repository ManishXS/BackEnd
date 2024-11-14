﻿using Microsoft.Azure.Cosmos;

namespace BackEnd.Entities
{
    public class CosmosDbContext
    {
        public Container UsersContainer { get; }
        public Container PostsContainer { get; }
        public Container FeedsContainer { get; }

        public CosmosDbContext(CosmosClient cosmosClient, IConfiguration configuration)
        {
            var databaseName = configuration["CosmosDbSettings:DatabaseName"];
            var usersContainerName = configuration["CosmosDbSettings:UsersContainerName"];
            var postsContainerName = configuration["CosmosDbSettings:PostsContainerName"];
            var feedsContainerName = configuration["CosmosDbSettings:FeedsContainerName"];

            UsersContainer = cosmosClient.GetContainer(databaseName, usersContainerName);
            PostsContainer = cosmosClient.GetContainer(databaseName, postsContainerName);
            FeedsContainer = cosmosClient.GetContainer(databaseName, feedsContainerName);
        }
    }
}
