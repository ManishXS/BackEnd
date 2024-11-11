using Azure.Identity;
using Azure.Storage.Blobs;
using BackEnd.Entities;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace BackEnd
{
    public class Startup
    {
        private readonly IConfiguration _configuration;

        public Startup(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public void ConfigureServices(IServiceCollection services)
        {
            try
            {
                Console.WriteLine("Starting ConfigureServices...");

                // Key Vault configuration
                var keyVaultEndpoint = new Uri("https://firstenxkv.vault.azure.net/");
                var updatedConfiguration = new ConfigurationBuilder()
                    .AddConfiguration(_configuration)
                    .AddAzureKeyVault(keyVaultEndpoint, new DefaultAzureCredential())
                    .Build();

                Console.WriteLine("Key Vault configuration loaded.");

                // Retrieve secrets
                var cosmosDbConnectionString = updatedConfiguration["CosmosDbConnectionString"];
                var blobConnectionString = updatedConfiguration["BlobConnectionString"];
                var apiKey = updatedConfiguration["ApiKey"];

                if (string.IsNullOrEmpty(cosmosDbConnectionString) || string.IsNullOrEmpty(blobConnectionString) || string.IsNullOrEmpty(apiKey))
                {
                    throw new Exception("Required configuration is missing. Check CosmosDbConnectionString, BlobConnectionString, and ApiKey1.");
                }

                // Cosmos DB configuration
                CosmosClientOptions clientOptions = new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Direct,
                    MaxRequestsPerTcpConnection = 10,
                    MaxTcpConnectionsPerEndpoint = 10
                };
                CosmosClient cosmosClient = new CosmosClient(cosmosDbConnectionString, clientOptions);
                services.AddSingleton(cosmosClient);
                services.AddScoped<CosmosDbContext>();

                Console.WriteLine("Cosmos DB client configured.");

                // Blob Storage configuration
                services.AddSingleton(x => new BlobServiceClient(blobConnectionString));
                Console.WriteLine("Blob Storage client configured.");

                // Configuration for dependency injection
                services.AddSingleton<IConfiguration>(updatedConfiguration);

                // Enable CORS
                services.AddCors(options =>
                {
                    options.AddPolicy("AllowAll", builder =>
                    {
                        builder.AllowAnyOrigin()
                               .AllowAnyHeader()
                               .AllowAnyMethod();
                    });
                });
                Console.WriteLine("CORS policy configured.");

                services.AddControllers();
                services.AddSwaggerGen();
                Console.WriteLine("Swagger configured.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in ConfigureServices: {ex.Message}");
                throw;
            }
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILogger<Startup> logger)
        {
            try
            {
                logger.LogInformation("Starting Configure...");


                if (env.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                    app.UseSwagger();
                    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "My API V1"));
                    logger.LogInformation("Development mode - Swagger UI enabled.");
                }

                app.UseHttpsRedirection();
                app.UseRouting();
                app.UseCors("AllowAll");
                app.UseMiddleware<ApiKeyMiddleware>();

                logger.LogInformation("Middleware configured.");

                app.UseAuthorization();
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                });

                logger.LogInformation("Application configured successfully.");
            }
            catch (Exception ex)
            {
                logger.LogError($"Error in Configure: {ex.Message}");
                throw;
            }
        }
    }
}
