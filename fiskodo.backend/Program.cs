using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;
using Lavalink4NET;
using Lavalink4NET.NetCord;
using Lavalink4NET.Extensions;
using Fiskodo.Services;

var builder = WebApplication.CreateBuilder(args);

// Controllers & Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// CORS for dashboard (Vite dev server)
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new()
    {
        Title = "Fiskodo Discord Music Bot API",
        Version = "v1"
    });

    // JWT bearer support in Swagger UI
    options.AddSecurityDefinition("Bearer", new()
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "JWT Bearer token. Example: 'Bearer {token}'"
    });

    options.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new()
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// JWT Authentication
var jwtSection = builder.Configuration.GetSection("Jwt");
var jwtIssuer = jwtSection["Issuer"] ?? "Fiskodo";
var jwtAudience = jwtSection["Audience"] ?? "Fiskodo.Api";
var jwtSecret = jwtSection["Secret"] ?? throw new InvalidOperationException("Jwt:Secret is not configured.");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });

// Discord Gateway client
builder.Services.AddSingleton<GatewayClient>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var token = config["Discord:Token"];

    if (string.IsNullOrWhiteSpace(token))
    {
        throw new InvalidOperationException("Discord:Token is not configured in appsettings.json or environment.");
    }

    var intents =
        GatewayIntents.Guilds |
        GatewayIntents.GuildMessages |
        GatewayIntents.MessageContent |
        GatewayIntents.GuildVoiceStates |
        GatewayIntents.GuildMessageReactions;

    var gatewayConfig = new GatewayClientConfiguration
    {
        Intents = intents,        
    };

    return new GatewayClient(new BotToken(token), gatewayConfig);
});

// Application command service (slash commands)
builder.Services.AddSingleton<ApplicationCommandService<ApplicationCommandContext>>(sp =>
{
    var service = new ApplicationCommandService<ApplicationCommandContext>();
    service.AddModules(typeof(Program).Assembly);
    return service;
});

// Voice gateway event bridge: forward NetCord VoiceStateUpdate/VoiceServerUpdate to Lavalink4NET
// so that JoinAsync can complete (required when not using NetCord.Hosting).
builder.Services.AddSingleton<NetCordVoiceBridge>(sp => new NetCordVoiceBridge(sp.GetRequiredService<GatewayClient>()));
builder.Services.AddSingleton<Lavalink4NET.Clients.IDiscordClientWrapper>(sp => sp.GetRequiredService<NetCordVoiceBridge>());

// Lavalink4NET configuration
builder.Services.AddLavalink();
builder.Services.ConfigureLavalink(options =>
{
    var section = builder.Configuration.GetSection("Lavalink");
    var baseAddress = section["BaseAddress"];
    if (!string.IsNullOrWhiteSpace(baseAddress))
    {
        options.BaseAddress = new Uri(baseAddress);
    }

    var passphrase = section["Passphrase"];
    if (!string.IsNullOrWhiteSpace(passphrase))
    {
        options.Passphrase = passphrase;
    }
    
});

// Playlist message store (guild -> channel/message for updating embed on auto-advance)
builder.Services.AddSingleton<PlaylistMessageStore>();

// Music service
builder.Services.AddSingleton<MusicService>();

// Auto-advance to next track when current finishes (polls every 2s)
builder.Services.AddHostedService<MusicQueueAdvanceService>();

// Voice cleanup: leave when idle (no track, empty queue) or when alone in channel for 1 minute
builder.Services.AddHostedService<VoiceChannelCleanupService>();

// Bot hosted service (registered as both IHostedService and concrete type so it can be injected)
builder.Services.AddSingleton<BotHostedService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BotHostedService>());

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors();

// Dashboard SPA (wwwroot: copied from fiskoda.dashboard/dist)
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// SPA fallback: non-API routes serve index.html
app.MapFallbackToFile("index.html");

app.Run();

