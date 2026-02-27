using System.Collections.Concurrent;
using NetCord;
using NetCord.Rest;

namespace Fiskodo.Services;

/// <summary>Builds the playlist control embed (title, now playing, queue count, shuffle, optional artwork thumbnail). Used when sending and when updating after button click.</summary>
public static class PlaylistEmbedBuilder
{
    public static EmbedProperties Build(string nowPlayingTitle, int queueCount, bool shuffle, string? artworkUrl = null)
    {
        var description = $"Now playing: **{nowPlayingTitle}**";
        var queueText = queueCount == 0 ? "Queue: empty" : $"Queue: {queueCount} track(s)";
        var shuffleText = shuffle ? "Shuffle: on" : "Shuffle: off";
        var embed = new EmbedProperties()
            .WithTitle("Playlist")
            .WithDescription($"{description}\n{queueText}\n{shuffleText}");
        if (!string.IsNullOrWhiteSpace(artworkUrl))
        {
            embed = embed.WithThumbnail(artworkUrl);
        }
        return embed;
    }

    /// <summary>Action row with Previous, Next, Shuffle buttons for playlist control message.</summary>
    public static ActionRowProperties CreateMusicControlsRow()
    {
        return new ActionRowProperties(new IActionRowComponentProperties[]
        {
            new ButtonProperties("music_previous", "Previous", ButtonStyle.Secondary),
            new ButtonProperties("music_skip", "Next", ButtonStyle.Secondary),
            new ButtonProperties("music_shuffle", "Shuffle", ButtonStyle.Secondary)
        });
    }
}

/// <summary>Maps a guild to its playlist control message (channel + message id) so the embed can be updated when the track changes (e.g. auto-advance).</summary>
public sealed class PlaylistMessageStore
{
    private readonly ConcurrentDictionary<ulong, (ulong ChannelId, ulong MessageId)> _byGuildId = new();

    public void RegisterByGuild(ulong guildId, ulong channelId, ulong messageId)
    {
        _byGuildId[guildId] = (channelId, messageId);
    }

    public bool TryGetByGuild(ulong guildId, out ulong channelId, out ulong messageId)
    {
        if (_byGuildId.TryGetValue(guildId, out var pair))
        {
            channelId = pair.ChannelId;
            messageId = pair.MessageId;
            return true;
        }
        channelId = 0;
        messageId = 0;
        return false;
    }

    public void UnregisterByGuild(ulong guildId)
    {
        _byGuildId.TryRemove(guildId, out _);
    }
}
