using Azure.Storage.Blobs;
using blob_demo.Models;
using blob_demo.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Configuration (NFR-2: no hardcoded secrets) ---
builder.Services.Configure<BlobStorageOptions>(
    builder.Configuration.GetSection(BlobStorageOptions.SectionName));

var blobOptions = builder.Configuration
    .GetSection(BlobStorageOptions.SectionName)
    .GetSection(BlobStorageOptions.SectionName) // Goes to the second layer
    .Get<BlobStorageOptions>() ?? new BlobStorageOptions();



// --- Azure SDK client (singleton, thread-safe) ---
builder.Services.AddSingleton(_ => new BlobServiceClient(blobOptions.ConnectionString));
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();

// --- Video storage (large media + range streaming) ---
builder.Services.Configure<VideoStorageOptions>(
    builder.Configuration.GetSection(VideoStorageOptions.SectionName));
builder.Services.AddScoped<IVideoStorageService, VideoStorageService>();

// Allow large multipart uploads (default Kestrel/form limits are ~28-30 MB).
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 2_147_483_648); // 2 GB
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 2_147_483_648; // 2 GB
});

// --- Order pipeline: Table + Queue (SDK clients are thread-safe -> singletons) ---
builder.Services.Configure<OrderPipelineOptions>(
    builder.Configuration.GetSection(OrderPipelineOptions.SectionName));
builder.Services.AddSingleton<IOrderTableStore, OrderTableStore>();
builder.Services.AddSingleton<IOrderQueueClient, OrderQueueClient>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddHostedService<OrderProcessingWorker>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

// FR-1.1: ensure the default container exists before serving traffic.
using (var scope = app.Services.CreateScope())
{
    var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorageService>();
    await blobs.EnsureContainerAsync();

    var videos = scope.ServiceProvider.GetRequiredService<IVideoStorageService>();
    await videos.EnsureContainerAsync();

    // FR-1.1 / FR-2.1: create OrdersTable and the queues on startup.
    var orders = scope.ServiceProvider.GetRequiredService<IOrderService>();
    await orders.InitializeAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Files/Index");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Files}/{action=Index}/{id?}");

app.Run();
