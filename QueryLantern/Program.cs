using QueryLantern.Components;
using QueryLantern.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Ancora agent runtime. The provider endpoint is read from configuration so the app stays
// runnable before a real provider profile is configured in the UI (later phases).
var baseUrl = builder.Configuration["Ancora:BaseUrl"] ?? "http://localhost:11434/v1";
var authEnvVar = builder.Configuration["Ancora:AuthEnvVar"] ?? "ANCORA_API_KEY";
var chatPath = builder.Configuration["Ancora:ChatCompletionsPath"] ?? "/v1/chat/completions";
builder.Services.AddSingleton(new AncoraRunner(baseUrl, authEnvVar, chatPath));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
