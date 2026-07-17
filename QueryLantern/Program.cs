using QueryLantern.Components;
using QueryLantern.Data;
using QueryLantern.Providers;
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
builder.Services.AddSingleton<SavedAnalysisRepository>();
builder.Services.AddSingleton<PlanRepository>();
builder.Services.AddSingleton<SchemaMemoryRepository>();
builder.Services.AddScoped<QueryLantern.Services.SchemaMemoryService>();
builder.Services.AddScoped<QueryLantern.Services.ConversationMemoryService>();

// Secret vault encrypts passwords and API keys at rest. The key lives in a local file outside the
// catalog so the catalog never contains plaintext secrets.
var keyPath = builder.Configuration["Vault:KeyPath"] ?? "vault.key";
builder.Services.AddSingleton(new SecretVault(keyPath));
builder.Services.AddSingleton<ProfileSecrets>();
builder.Services.AddSingleton<SettingsService>();

// Local Ed25519 identity signs the activity journal. The private key lives in a local file so the
// journal is verifiable as having been produced by this install.
var identityPath = builder.Configuration["Identity:KeyPath"] ?? "identity.key";
builder.Services.AddSingleton(new IdentityService(identityPath));
var journalPath = builder.Configuration["Journal:Path"] ?? "activity.journal";
builder.Services.AddSingleton<ActivityJournal>(sp => new ActivityJournal(journalPath, sp.GetRequiredService<IdentityService>()));
var costPath = builder.Configuration["Cost:Path"] ?? "cost.json";
builder.Services.AddSingleton(new CostService(costPath));
builder.Services.AddSingleton<SchemaCache>();
builder.Services.AddSingleton<SchemaService>();
builder.Services.AddHttpClient<ProviderClient>();
builder.Services.AddSingleton<ModelRouter>();
builder.Services.AddSingleton<HumanInTheLoop>();
builder.Services.AddSingleton<ApprovalService>();
builder.Services.AddSingleton<LocalFirstService>();

// Ancora agent runtime. The provider endpoint is read from configuration so the app stays
// runnable before a real provider profile is configured in the UI (later phases).
var baseUrl = builder.Configuration["Ancora:BaseUrl"] ?? "http://localhost:11434/v1";
var authEnvVar = builder.Configuration["Ancora:AuthEnvVar"] ?? "ANCORA_API_KEY";
var chatPath = builder.Configuration["Ancora:ChatCompletionsPath"] ?? "/v1/chat/completions";
builder.Services.AddSingleton<AncoraRunner>();
builder.Services.AddScoped<QueryLantern.Tools.AgentToolbox>();
builder.Services.AddScoped<QueryLantern.Tools.PlannerTool>();
builder.Services.AddScoped<QueryLantern.Services.GraphRunService>();
builder.Services.AddScoped<QueryLantern.Services.OrchestrationService>();
builder.Services.AddScoped<QueryLantern.Services.AnswerGroundingService>();
builder.Services.AddScoped<QueryLantern.Services.AnswerCriticService>();
builder.Services.AddScoped<QueryLantern.Services.ConfidenceService>();
builder.Services.AddScoped<QueryLantern.Services.DecompositionService>();
builder.Services.AddScoped<QueryLantern.Services.PlanService>();
builder.Services.AddScoped<ChatService>();

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
