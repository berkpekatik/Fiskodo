using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetCord.Gateway;
using NetCord.Rest;

namespace Fiskodo.Services;

/// <summary>
/// Periodically checks guilds with a queue and advances to the next track when the current one has finished.
/// Updates the playlist embed message when a track auto-advances so the UI shows the new "Now playing".
/// </summary>
public sealed class MusicQueueAdvanceService : BackgroundService
{
    private readonly ILogger<MusicQueueAdvanceService> _logger;
    private readonly MusicService _musicService;
    private readonly PlaylistMessageStore _playlistMessageStore;
    private readonly GatewayClient _client;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    public MusicQueueAdvanceService(
        ILogger<MusicQueueAdvanceService> logger,
        MusicService musicService,
        PlaylistMessageStore playlistMessageStore,
        GatewayClient client)
    {
        _logger = logger;
        _musicService = musicService;
        _playlistMessageStore = playlistMessageStore;
        _client = client;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(CheckInterval, stoppingToken).ConfigureAwait(false);
                var guildIds = _musicService.GetGuildIdsWithQueue();
                foreach (var guildId in guildIds)
                {
                    try
                    {
                        var advanced = await _musicService.TryAdvanceToNextAsync(guildId, stoppingToken).ConfigureAwait(false);
                        if (advanced)
                            await UpdatePlaylistEmbedIfExistsAsync(guildId, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Advance next failed for guild {GuildId}", guildId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Music queue advance loop error");
            }
        }
    }

    private async Task UpdatePlaylistEmbedIfExistsAsync(ulong guildId, CancellationToken cancellationToken)
    {
        if (!_playlistMessageStore.TryGetByGuild(guildId, out var channelId, out var messageId))
            return;
        try
        {
            var (nowPlayingTitle, queueCount, shuffle, artworkUrl) = await _musicService.GetPlaylistEmbedInfoAsync(guildId, cancellationToken).ConfigureAwait(false);
            var embed = PlaylistEmbedBuilder.Build(nowPlayingTitle ?? "Nothing", queueCount, shuffle, artworkUrl);
            await _client.Rest.ModifyMessageAsync(channelId, messageId, m =>
            {
                m.Embeds = new[] { embed };
                m.Components = new[] { PlaylistEmbedBuilder.CreateMusicControlsRow() };
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Update playlist embed failed for guild {GuildId}", guildId);
        }
    }
}
