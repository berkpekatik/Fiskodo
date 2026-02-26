using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;

namespace Fiskodo.Services;

/// <summary>
/// Runs independently of the queue: leaves voice when the bot is idle (no track, empty queue)
/// or when the bot has been alone in the channel for one minute.
/// </summary>
public sealed class VoiceChannelCleanupService : BackgroundService
{
    private readonly ILogger<VoiceChannelCleanupService> _logger;
    private readonly GatewayClient _client;
    private readonly NetCordVoiceBridge _voiceBridge;
    private readonly MusicService _musicService;
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _aloneSincePerGuild = new();
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan AloneTimeout = TimeSpan.FromMinutes(1);

    public VoiceChannelCleanupService(
        ILogger<VoiceChannelCleanupService> logger,
        GatewayClient client,
        NetCordVoiceBridge voiceBridge,
        MusicService musicService)
    {
        _logger = logger;
        _client = client;
        _voiceBridge = voiceBridge;
        _musicService = musicService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var botUserId = _client.Id;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false);

                foreach (var (guildId, guild) in _client.Cache.Guilds)
                {
                    try
                    {
                        if (!guild.VoiceStates.TryGetValue(botUserId, out var botState) || !botState.ChannelId.HasValue)
                            continue;

                        var channelId = botState.ChannelId.Value;
                        int usersInChannel = guild.VoiceStates.Values.Count(v => v.ChannelId == channelId);
                        bool alone = usersInChannel <= 1;

                        if (await _musicService.IsIdleAsync(guildId, stoppingToken).ConfigureAwait(false))
                        {
                            await LeaveAndStopAsync(guildId, stoppingToken).ConfigureAwait(false);
                            _aloneSincePerGuild.TryRemove(guildId, out _);
                            continue;
                        }

                        if (alone)
                        {
                            var now = DateTimeOffset.UtcNow;
                            var since = _aloneSincePerGuild.GetOrAdd(guildId, now);
                            if (now - since >= AloneTimeout)
                            {
                                _logger.LogInformation("Auto-leaving guild {GuildId}: alone in channel for {Minutes} minute(s)", guildId, AloneTimeout.TotalMinutes);
                                await LeaveAndStopAsync(guildId, stoppingToken).ConfigureAwait(false);
                                _aloneSincePerGuild.TryRemove(guildId, out _);
                            }
                        }
                        else
                        {
                            _aloneSincePerGuild.TryRemove(guildId, out _);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Voice cleanup check failed for guild {GuildId}", guildId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Voice channel cleanup loop error");
            }
        }
    }

    private async Task LeaveAndStopAsync(ulong guildId, CancellationToken cancellationToken)
    {
        await _voiceBridge.SendVoiceUpdateAsync(guildId, voiceChannelId: null, cancellationToken: cancellationToken).ConfigureAwait(false);
        await _musicService.StopAsync(guildId, cancellationToken).ConfigureAwait(false);
    }
}
