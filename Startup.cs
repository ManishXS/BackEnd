using Azure.Identity;
using Azure.Storage.Blobs;
using BackEnd.Entities;
using Microsoft.Azure.Cosmos;

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

               var appConfigConnectionString = "Endpoint=https://azurermtenx.azconfig.io;" +
                                 "Id=8FPB;" +
                                 "Secret=" +
                                 "3NCoPOSo0Y1ykrX6ih9ObYVbY2ZA6RLqaXyMyBI04eB5k4wkhpA5JQQJ99AKACGhslBY0DYHAAACAZAC1woJ";

                var updatedConfiguration = new ConfigurationBuilder()
                    .AddConfiguration(_configuration)
                    .AddAzureAppConfiguration(options =>
                    {
                        options.Connect(appConfigConnectionString)
                               .ConfigureKeyVault(kv => kv.SetCredential(new DefaultAzureCredential())); 
                    })
                    .Build();

                var cosmosDbConnectionString = updatedConfiguration["CosmosDbConnectionString"];
                var blobConnectionString = updatedConfiguration["BlobConnectionString"];
                var apiKey = updatedConfiguration["ApiKey"];

                if (string.IsNullOrEmpty(cosmosDbConnectionString) || string.IsNullOrEmpty(blobConnectionString) || string.IsNullOrEmpty(apiKey))
                {
                    throw new Exception("Required configuration is missing. Check CosmosDbConnectionString, BlobConnectionString, and ApiKey.");
                }

                CosmosClientOptions clientOptions = new CosmosClientOptions
                {
                    ConnectionMode = ConnectionMode.Direct,
                    MaxRequestsPerTcpConnection = 10,
                    MaxTcpConnectionsPerEndpoint = 10
                };
                CosmosClient cosmosClient = new CosmosClient(cosmosDbConnectionString, clientOptions);
                services.AddSingleton(cosmosClient);
                services.AddScoped<CosmosDbContext>();

                services.AddSingleton(x => new BlobServiceClient(blobConnectionString));

                services.AddSingleton<IConfiguration>(updatedConfiguration);

                //services.AddCors(options =>
                //{
                //    options.AddPolicy("AllowAll", builder =>
                //    {
                //        builder.AllowAnyOrigin()
                //               .AllowAnyHeader()
                //               .AllowAnyMethod();
                //    });
                //});
                services.AddSignalR();
                services.AddControllers();
                services.AddSwaggerGen();

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
                app.UseMiddleware<SkipAuthorizationMiddleware>();
                app.UseCors(builder => builder
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .SetIsOriginAllowed((host) => true)
                        .AllowCredentials());

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
