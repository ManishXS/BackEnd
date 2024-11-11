using Newtonsoft.Json;

namespace BackEnd.Entities
{
    public class User
    {
        [JsonProperty(PropertyName = "id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        [JsonProperty(PropertyName = "username")]
        public string Username { get; set; } = "Anonymous";

        [JsonProperty(PropertyName = "profilePicUrl")]
        public string ProfilePicUrl { get; internal set; }

        [JsonProperty(PropertyName = "createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
