using System.Collections.Immutable;
using Lavalink4NET.Clients;
using Lavalink4NET.Clients.Events;
using Lavalink4NET.Events;
using NetCord.Gateway;

namespace Fiskodo.Services;

/// <summary>
/// Forwards NetCord gateway voice events (VoiceStateUpdate, VoiceServerUpdate) to Lavalink4NET
/// so that the player can be retrieved when joining a voice channel.
/// Required when not using NetCord.Hosting (Generic Host) which would wire this automatically.
/// </summary>
public sealed class NetCordVoiceBridge : IDiscordClientWrapper
{
    private readonly GatewayClient _client;
    private readonly TaskCompletionSource _ready = new();

    public NetCordVoiceBridge(GatewayClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;

        _client.VoiceStateUpdate += OnVoiceStateUpdateAsync;
        _client.VoiceServerUpdate += OnVoiceServerUpdateAsync;
        _client.Ready += OnReady;
    }

    public event AsyncEventHandler<VoiceStateUpdatedEventArgs>? VoiceStateUpdated;
    public event AsyncEventHandler<VoiceServerUpdatedEventArgs>? VoiceServerUpdated;

    private ValueTask OnReady(ReadyEventArgs _)
    {
        _ready.TrySetResult();
        return default;
    }

    private ValueTask OnVoiceServerUpdateAsync(VoiceServerUpdateEventArgs e)
    {
        if (e.Endpoint is null)
            return default;

        var args = new VoiceServerUpdatedEventArgs(
            e.GuildId,
            new VoiceServer(e.Token, e.Endpoint));

        return VoiceServerUpdated.InvokeAsync(this, args);
    }

    private async ValueTask OnVoiceStateUpdateAsync(NetCord.Gateway.VoiceState e)
    {
        ArgumentNullException.ThrowIfNull(e);

        Lavalink4NET.Clients.VoiceState previous = default;
        if (_client.Cache.Guilds.TryGetValue(e.GuildId, out var guild) &&
            guild.VoiceStates.TryGetValue(e.UserId, out var prev))
        {
            previous = new Lavalink4NET.Clients.VoiceState(prev.ChannelId, prev.SessionId);
        }

        var currentUserId = _client.Id;
        var updated = new Lavalink4NET.Clients.VoiceState(e.ChannelId, e.SessionId);
        var args = new VoiceStateUpdatedEventArgs(
            e.GuildId,
            e.UserId,
            e.UserId == currentUserId,
            updated,
            previous);

        await VoiceStateUpdated.InvokeAsync(this, args).ConfigureAwait(false);
    }

    public ValueTask<ImmutableArray<ulong>> GetChannelUsersAsync(
        ulong guildId,
        ulong voiceChannelId,
        bool includeBots = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_client.Cache.Guilds.TryGetValue(guildId, out var guild))
            return new ValueTask<ImmutableArray<ulong>>(ImmutableArray<ulong>.Empty);

        var currentUserId = _client.Id;
        var userIds = guild.VoiceStates.Values
            .Where(x => x.ChannelId == voiceChannelId && x.UserId != currentUserId)
            .Where(x => includeBots || x.User is not { IsBot: true })
            .Select(x => x.UserId)
            .ToImmutableArray();

        return new ValueTask<ImmutableArray<ulong>>(userIds);
    }

    public async ValueTask SendVoiceUpdateAsync(
        ulong guildId,
        ulong? voiceChannelId,
        bool selfDeaf = false,
        bool selfMute = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var props = new VoiceStateProperties(guildId, voiceChannelId)
        {
            SelfDeaf = selfDeaf,
            SelfMute = selfMute
        };

        await _client.UpdateVoiceStateAsync(props).ConfigureAwait(false);
    }

    public async ValueTask<ClientInformation> WaitForReadyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _ready.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        var shardCount = _client.Shard?.Count ?? 1;
        return new ClientInformation("NetCord", _client.Id, shardCount);
    }
}
