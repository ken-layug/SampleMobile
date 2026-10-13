using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SampleMobile.Application;
using SampleMobile.Application.Common;
using SampleMobile.Infrastructure;
using SampleMobile.Infrastructure.Authentication;
using SampleMobile.Infrastructure.Persistence.Seeding;
using SampleMobile.Web.Common;
using SampleMobile.Web.Components;
using SampleMobile.Web.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddApplication();
builder.AddInfrastructure();

// --- Blazor dashboard (per-page Interactive Server).
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddSingleton<IDashboardSender, DashboardSender>();

// --- Mobile REST API.
builder.Services.AddControllers(options =>
        // Validation errors keyed by JSON names ("password"), matching the keys handlers emit.
        options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider()))
    .AddJsonOptions(options => JsonDefaults.Configure(options.JsonSerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOpenApi(options => {
    options.ShouldInclude = description => description.RelativePath?.StartsWith("api/", StringComparison.Ordinal) == true;
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
    options.AddOperationTransformer<BearerSecurityRequirementTransformer>();
});

// --- Authentication: Identity cookie for the dashboard (default), JWT bearer for the mobile API.
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme)
    .AddIdentityCookies();
builder.Services.AddAuthentication()
    .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, _ => { });
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtSigningKeyProvider, IOptions<JwtOptions>>((options, keys, jwt) => {
        // Keep claim names exactly as issued ("sub", "role") instead of mapping them to legacy URIs.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters {
            ValidIssuer = jwt.Value.Issuer,
            ValidAudience = jwt.Value.Audience,
            IssuerSigningKey = keys.Key,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.UniqueName,
            RoleClaimType = "role"
        };
    });
builder.Services.ConfigureApplicationCookie(options => {
    options.Cookie.Name = "SampleMobile.Admin";
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
    options.AccessDeniedPath = "/account/access-denied";
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.AdminDashboard, policy => policy
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
        .RequireAuthenticatedUser()
        .RequireRole(Roles.Admin))
    .AddPolicy(AuthorizationPolicies.MobileApi, policy => policy
        .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser());

var app = builder.Build();

// Migrations and demo data, as configured (Development only for the demo accounts).
await app.Services.InitializeDatabaseAsync();

// API routes answer with RFC 9457 problem details, never HTML error pages or redirects.
app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    api => {
        api.UseExceptionHandler();
        api.UseStatusCodePages();
    });
app.UseWhen(
    context => !context.Request.Path.StartsWithSegments("/api"),
    web => {
        if (!app.Environment.IsDevelopment()) {
            web.UseExceptionHandler("/Error", createScopeForErrors: true);
        }

        web.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
    });

if (!app.Environment.IsDevelopment()) {
    // Development stays on plain HTTP as well, so an Android emulator can reach http://10.0.2.2:5010.
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapControllers();

if (app.Environment.IsDevelopment()) {
    app.MapOpenApi();
    app.MapScalarApiReference(options => options.WithTitle("SampleMobile mobile API"));
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

/// <summary>Exposed so integration tests can host the app with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
