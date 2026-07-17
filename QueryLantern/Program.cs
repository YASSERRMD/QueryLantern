using QueryLantern.Components;
using QueryLantern.Data;
using QueryLantern.Schema;
using QueryLantern.Security;
using QueryLantern.Services;
using QueryLantern.Settings;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Local SQLite catalog for saved connections and provider profiles. Path is configurable so the
// app stays runnable: it bootstraps its tables on first use.
var catalogPath = builder.Configuration["Catalog:Path"] ?? "catalog.db";
builder.Services.AddSingleton(new CatalogStore(catalogPath));
builder.Services.AddSingleton<ConnectionRepository>();
builder.Services.AddSingleton<ProviderRepository>();

// Secret vault encrypts passwords and API keys at rest. The key lives in a local file outside the
// catalog so the catalog never contains plaintext secrets.
var keyPath = builder.Configuration["Vault:KeyPath"] ?? "vault.key";
builder.Services.AddSingleton(new SecretVault(keyPath));
builder.Services.AddSingleton<ProfileSecrets>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<SchemaCache>();
builder.Services.AddSingleton<SchemaService>();

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
