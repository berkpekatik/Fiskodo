using System.Net.WebSockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services;
using NetCord.Services.ApplicationCommands;

namespace Fiskodo.Services;

public sealed class BotHostedService : IHostedService
{
    private readonly ILogger<BotHostedService> _logger;
    private readonly GatewayClient _client;
    private readonly ApplicationCommandService<ApplicationCommandContext> _commandService;
    private readonly MusicService _musicService;
    private readonly IServiceProvider _serviceProvider;

    public BotHostedService(
        ILogger<BotHostedService> logger,
        GatewayClient client,
        ApplicationCommandService<ApplicationCommandContext> commandService,
        MusicService musicService,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _client = client;
        _commandService = commandService;
        _musicService = musicService;
        _serviceProvider = serviceProvider;
    }

    public DateTimeOffset? StartedAt { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting Discord GatewayClient...");

        _client.InteractionCreate += OnInteractionCreateAsync;

        // Register slash commands with Discord
        _logger.LogInformation("Registering application commands...");
        await _commandService.RegisterCommandsAsync(_client.Rest, _client.Id, cancellationToken: cancellationToken).ConfigureAwait(false);

        // Start the gateway connection
        await _client.StartAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        StartedAt = DateTimeOffset.UtcNow;

        _logger.LogInformation("Discord GatewayClient started.");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping Discord GatewayClient...");

        _client.InteractionCreate -= OnInteractionCreateAsync;

        try
        {
            await _client.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while closing Discord gateway connection.");
        }
    }

    private ValueTask OnInteractionCreateAsync(Interaction interaction)
    {
        if (interaction is ApplicationCommandInteraction applicationCommandInteraction)
        {
            var context = new ApplicationCommandContext(applicationCommandInteraction, _client);
            _ = ExecuteCommandAsync(applicationCommandInteraction, context);
            return ValueTask.CompletedTask;
        }

        if (interaction is MessageComponentInteraction componentInteraction)
        {
            _ = ExecuteComponentAsync(componentInteraction);
            return ValueTask.CompletedTask;
        }

        return ValueTask.CompletedTask;
    }

    private async Task ExecuteComponentAsync(MessageComponentInteraction interaction)
    {
        var customId = interaction.Data.CustomId;
        var guildId = interaction.GuildId;
        if (!guildId.HasValue)
        {
            await interaction.SendResponseAsync(InteractionCallback.Message("This button can only be used in a server.")).ConfigureAwait(false);
            return;
        }

        try
        {
            switch (customId)
            {
                case "music_skip":
                    await _musicService.SkipAsync(guildId.Value).ConfigureAwait(false);
                    break;
                case "music_previous":
                    await _musicService.PreviousAsync(guildId.Value).ConfigureAwait(false);
                    break;
                case "music_shuffle":
                    _musicService.ToggleShuffle(guildId.Value);
                    break;
                default:
                    return;
            }

            var (nowPlayingTitle, queueCount, shuffle, artworkUrl) = await _musicService.GetPlaylistEmbedInfoAsync(guildId.Value).ConfigureAwait(false);
            var embed = PlaylistEmbedBuilder.Build(nowPlayingTitle ?? "Nothing", queueCount, shuffle, artworkUrl);
            await interaction.SendResponseAsync(InteractionCallback.ModifyMessage(m =>
            {
                m.Embeds = new[] { embed };
                m.Components = new[] { PlaylistEmbedBuilder.CreateMusicControlsRow() };
            })).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Music button handling failed");
            try
            {
                await interaction.SendResponseAsync(InteractionCallback.Message("Something went wrong.")).ConfigureAwait(false);
            }
            catch
            {
                // Already responded or expired
            }
        }
    }

    private async Task ExecuteCommandAsync(ApplicationCommandInteraction interaction, ApplicationCommandContext context)
    {
        try
        {
            var result = await _commandService.ExecuteAsync(context, _serviceProvider).ConfigureAwait(false);

            if (result is not IFailResult failResult)
                return;

            await interaction.SendResponseAsync(InteractionCallback.Message(failResult.Message)).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Slash command execution failed");
            try
            {
                await interaction.SendResponseAsync(InteractionCallback.Message("An error occurred while running the command.")).ConfigureAwait(false);
            }
            catch
            {
                // Already responded or interaction expired
            }
        }
    }
}

