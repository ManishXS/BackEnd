using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;

namespace BackEnd
{
    public class ApiKeyMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly string _apiKey;

        public ApiKeyMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _apiKey = configuration["ApiKey"] ?? throw new Exception("API key is missing from configuration.");
        }

        public async Task InvokeAsync(HttpContext context)
        {
            //var apiKey = context.Request.Headers["X-API-KEY"].FirstOrDefault();
            //if (string.IsNullOrEmpty(apiKey))
            //{
            //    context.Response.StatusCode = 401;
            //    await context.Response.WriteAsync("API Key is missing.");
            //    return;
            //}
            //if (!string.Equals(apiKey, _apiKey, StringComparison.Ordinal))
            //{
            //    context.Response.StatusCode = 403;
            //    await context.Response.WriteAsync("Unauthorized client.");
            //    return;
            //}
            await _next(context);
        }
    }
}
