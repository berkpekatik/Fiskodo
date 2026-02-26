using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Lavalink4NET;
using NetCord.Gateway;
using NetCord;

namespace Fiskodo.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class BotStatusController : ControllerBase
{
    private readonly GatewayClient _client;
    private readonly Services.BotHostedService _botHostedService;
    private readonly Services.MusicService _musicService;

    public BotStatusController(GatewayClient client, Services.BotHostedService botHostedService, Services.MusicService musicService)
    {
        _client = client;
        _botHostedService = botHostedService;
        _musicService = musicService;
    }

    /// <summary>Current playback state for a voice connection (null title + 0 queue = idle).</summary>
    public sealed record PlaybackInfoDto(
        string? NowPlayingTitle,
        int QueueCount,
        bool IsPlaylistSession,
        bool Shuffle);

    /// <summary>One voice connection: bot is in this guild's voice channel.</summary>
    public sealed record VoiceConnectionDto(
        ulong GuildId,
        string? GuildName,
        string? GuildIconUrl,
        ulong ChannelId,
        string? ChannelName,
        /// <summary>Number of users in this voice channel (including the bot). 1 = bot alone.</summary>
        int UserCount,
        PlaybackInfoDto Playback);

    public sealed record BotStatusDto(
        /// <summary>Discord user ID of the bot.</summary>
        ulong BotUserId,
        /// <summary>Current shard index (null if not sharded).</summary>
        int? ShardId,
        /// <summary>Total shard count (1 if not sharded).</summary>
        int ShardCount,
        int ConnectedGuilds,
        int ActiveVoiceConnections,
        TimeSpan Uptime,
        /// <summary>Gateway round-trip latency.</summary>
        TimeSpan Latency,
        IReadOnlyList<VoiceConnectionDto> VoiceConnections);

    [HttpGet]
    public async Task<ActionResult<BotStatusDto>> GetStatus(CancellationToken cancellationToken = default)
    {
        var botUserId = _client.Id;
        var guilds = _client.Cache.Guilds;
        var connectedGuilds = guilds.Count;

        var voiceConnections = new List<VoiceConnectionDto>();
        foreach (var (guildId, guild) in guilds)
        {
            if (!guild.VoiceStates.TryGetValue(botUserId, out var voiceState) || !voiceState.ChannelId.HasValue)
                continue;

            var channelId = voiceState.ChannelId.Value;
            int userCount = guild.VoiceStates.Values.Count(v => v.ChannelId == channelId);
            string? channelName = null;
            if (guild.Channels?.TryGetValue(channelId, out var channel) == true && channel is not null)
                channelName = channel.Name;

            var (nowPlayingTitle, queueCount, isPlaylistSession, shuffle) = await _musicService.GetPlaybackStatusAsync(guildId, cancellationToken).ConfigureAwait(false);
            var playback = new PlaybackInfoDto(nowPlayingTitle, queueCount, isPlaylistSession, shuffle);

            voiceConnections.Add(new VoiceConnectionDto(
                GuildId: guildId,
                GuildName: guild.Name,
                GuildIconUrl: guild.GetIconUrl()?.ToString(),
                ChannelId: channelId,
                ChannelName: channelName,
                UserCount: userCount,
                Playback: playback));
        }

        var startedAt = _botHostedService.StartedAt ?? DateTimeOffset.UtcNow;
        var uptime = DateTimeOffset.UtcNow - startedAt;
        var latency = _client.Latency;
        var shard = _client.Shard;

        var dto = new BotStatusDto(
            BotUserId: botUserId,
            ShardId: shard?.Id,
            ShardCount: shard?.Count ?? 1,
            ConnectedGuilds: connectedGuilds,
            ActiveVoiceConnections: voiceConnections.Count,
            Uptime: uptime,
            Latency: latency,
            VoiceConnections: voiceConnections);

        return Ok(dto);
    }
}

