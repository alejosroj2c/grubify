using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Caching.Memory;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Add in-memory cache with strict size limit for cart storage
// Hardened after OOM incident (May 26 2026): reduced from 500 to 100,
// added compaction to auto-evict entries before hitting limit
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 100; // Max 100 active carts in memory
    options.CompactionPercentage = 0.25; // Evict 25% of entries when limit is reached
});

// Configure forwarded headers for Azure Container Apps
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

// Add CORS — reads AllowedOrigins from config/env vars (e.g. AllowedOrigins__0)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() 
                ?? Array.Empty<string>();
            
            // Always include localhost for development
            var origins = new HashSet<string>(allowedOrigins)
            {
                "http://localhost:3000",
                "https://localhost:3000"
            };
            
            // Remove empty entries
            origins.RemoveWhere(string.IsNullOrWhiteSpace);
            
            if (origins.Count > 0)
            {
                policy.WithOrigins(origins.ToArray())
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            }
            else
            {
                // Fallback: allow any origin without credentials
                policy.AllowAnyOrigin()
                      .AllowAnyHeader()
                      .AllowAnyMethod();
            }
        });
});

var app = builder.Build();

// Use forwarded headers for Azure Container Apps
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Remove UseHttpsRedirection for Azure Container Apps - ACA handles TLS termination
// app.UseHttpsRedirection();

// Use CORS
app.UseCors("AllowReactApp");

app.UseAuthorization();

app.MapControllers();

app.Run();
