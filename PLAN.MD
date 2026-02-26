# Project Context & Goal
I am building a modern Discord Music Bot using C# and .NET 8/9. The project will be an ASP.NET Core Web API application that acts as a hybrid: it will run a Discord bot in the background (via HostedService/Worker) and serve a Web API for external monitoring and control.

# Tech Stack
- Framework: ASP.NET Core Web API
- Discord Wrapper: NetCord
- Music Wrapper: Lavalink4NET (integrated with NetCord)
- API Security: JWT Bearer Authentication
- API Documentation: Swagger / OpenAPI
- Database: NONE for now (No Entity Framework, no MySQL/Pomelo). Keep it stateless.

# Architecture & Design Rules (STRICT)
1. Layering: Keep it simple. Use a single project structure. All business logic should reside in a `Services/` folder. Do NOT create a separate Infrastructure or Domain layer.
2. OOP & SOLID: Follow SOLID principles but be pragmatic. 
3. Interface Usage: DO NOT over-abstract. Do not create interfaces (e.g., IMusicService) unless the class will genuinely have multiple implementations or is strictly required for an external library. Inject concrete classes (e.g., MusicService) directly into the DI container if they are single-purpose.
4. Dependency Injection: Heavily utilize ASP.NET Core's native DI (`IServiceCollection`).

# Required Components to Implement

## 1. Bot & Music Services (`Services/` folder)
- `BotHostedService`: An `IHostedService` that initializes NetCord's `GatewayClient`, connects to Discord, and manages connection lifecycle without blocking the Web API thread.
- `MusicService`: A concrete class handling Lavalink connections, track resolving, and player state (Play, Pause, Skip, Queue management).
- `InteractionModule` (or NetCord's equivalent Application Command modules): For handling Discord Slash Commands.

## 2. Web API (`Controllers/` folder)
- `AuthController`: An endpoint to generate a JWT token (using hardcoded credentials in appsettings.json for now) to access secured endpoints.
- `BotStatusController`: A [Authorize] protected endpoint that returns current bot stats (e.g., Connected Guilds, Active Voice Connections, Uptime, Latency).

## 3. Configuration (`Program.cs` & `appsettings.json`)
- Configure NetCord Token and Intents.
- Configure Lavalink Node details (Host, Port, Password).
- Configure JWT Authentication (Issuer, Audience, Secret Key).
- Add Swagger generation configured to accept JWT Bearer tokens in the UI.

# Execution Step
Start by generating the `appsettings.json` structure and the `Program.cs` file wiring up NetCord, Lavalink4NET, JWT, and Swagger. Then proceed to the `Services`.

Also after that create a documentation for api, everytime update the doc.