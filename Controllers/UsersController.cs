using Microsoft.AspNetCore.Mvc;
using BackEnd.Entities;
using Microsoft.Azure.Cosmos;
using System.Resources;
using User = BackEnd.Entities.User;
using System.Collections;
using System.Reflection;

namespace BackEnd.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly CosmosDbContext _dbContext;
        private static readonly Random _random = new Random();

        private static readonly string _storageBaseUrl = "https://storagetenxs.blob.core.windows.net/profilepic/";

        public UsersController(CosmosDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet("create")]
        public async Task<IActionResult> CreateUser()
        {
            try
            {
                var newUser = new User
                {
                    Username = GenerateRandomName(),
                    ProfilePicUrl = GetRandomProfilePic()
                };
                await _dbContext.UsersContainer.CreateItemAsync(newUser);

                // Return the user details including the profile picture
                return Ok(new
                {
                    userId = newUser.Id,
                    username = newUser.Username,
                    profilePic = newUser.ProfilePicUrl,
                    createdAt = newUser.CreatedAt
                });
            }
            catch (CosmosException ex)
            {
                return StatusCode(500, $"Cosmos DB Error: {ex.Message}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"An error occurred: {ex.Message}");
            }
        }

        private string GenerateRandomName()
        {
            string adjective = GetRandomResource("Adj_");
            string noun = GetRandomResource("Noun_");

            // Make sure at least one word is French
            bool isFrenchInAdjective = _random.Next(2) == 0;
            string finalAdjective = isFrenchInAdjective ? GetFrenchPart(adjective) : GetEnglishPart(adjective);
            string finalNoun = isFrenchInAdjective ? GetEnglishPart(noun) : GetFrenchPart(noun);

            return $"{finalAdjective}_{finalNoun}";
        }

        private string GetRandomProfilePic()
        {
            int randomNumber = _random.Next(1, 26); 
            return $"{_storageBaseUrl}pp{randomNumber}.jpg";
        }

        private string GetRandomResource(string resourceType)
        {
            ResourceManager resourceManager = new ResourceManager("BackEnd.Resources.AdjectivesNouns", Assembly.GetExecutingAssembly());
            var resourceSet = resourceManager.GetResourceSet(System.Globalization.CultureInfo.CurrentUICulture, true, true);

            if (resourceSet == null)
            {
                throw new Exception("ResourceSet is null. Resource file might not be found.");
            }

            var matchingEntries = new List<DictionaryEntry>();
            foreach (DictionaryEntry entry in resourceSet)
            {
                if (entry.Key.ToString().StartsWith(resourceType))
                {
                    matchingEntries.Add(entry);
                }
            }

            if (matchingEntries.Count == 0)
            {
                throw new Exception($"No matching {resourceType} resources found.");
            }

            DictionaryEntry selectedEntry = matchingEntries[_random.Next(matchingEntries.Count)];

            if (selectedEntry.Value != null && selectedEntry.Key != null)
            {
                return $"{selectedEntry.Key}-{selectedEntry.Value}";
            }

            throw new Exception("Invalid resource entry detected.");
        }
        private string GetFrenchPart(string entry)
        {
            var parts = entry?.Split('-');
            return parts?[0].Split('_')[1];
        }

        private string GetEnglishPart(string entry)
        {
            var parts = entry?.Split('-');
            return parts?[1];
        }
    }
}
