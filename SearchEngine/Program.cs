using SearchEngine.Components;
using System.ClientModel;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// REPLACE WITH YOUR OWN CONNECTION STRING
builder.Services.AddDbContextFactory<ForumDb>(o => o.UseNpgsql(
    "Host=localhost;Port=5432;Database=forum;Username=postgres;Password=pg", n => n.UseVector()));

builder.Services.AddMediatR(c => c.RegisterServicesFromAssemblyContaining<Program>());

// REPLACE WITH YOUR OWN LLAMA.CPP URI AND EMBEDDINGGEMMA2 MODEL NAME/QUANTIZATION
builder.Services.AddSingleton(
    new OpenAIClient(new ApiKeyCredential("none"),
            new OpenAIClientOptions { Endpoint = new Uri("http://localhost:8080/v1") })
        .GetEmbeddingClient("embeddinggemma2")
        .AsIEmbeddingGenerator());

// REPLACE WITH YOUR OWN LLAMA.CPP URI
builder.Services.AddHttpClient<Embedder>(c => c.BaseAddress = new Uri("http://localhost:8080"));

var app = builder.Build();

using (var db = app.Services.GetRequiredService<IDbContextFactory<ForumDb>>().CreateDbContext())
    db.Database.EnsureCreated(); // NO NEED FOR MIGRATIONS, JUST CREATE THE TABLES ON STARTUP (BAD FOR PRODUCTION, FINE FOR DEMO)

app.MapGet("/posts/{id:int}/image", async (int id, IDbContextFactory<ForumDb> f) =>
{
    await using var db = f.CreateDbContext();
    var p = await db.Posts.Where(x => x.Id == id)
        .Select(x => new { x.Image, x.ImageType }).FirstOrDefaultAsync();
    return p?.Image is null ? Results.NotFound() : Results.File(p.Image, p.ImageType);
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
