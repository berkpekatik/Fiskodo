using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using NetCord.Services.ApplicationCommands;

namespace Fiskodo.Services;

public sealed class MusicCommandModule : ApplicationCommandModule<ApplicationCommandContext>
{
    private readonly MusicService _musicService;
    private readonly PlaylistMessageStore _playlistMessageStore;

    public MusicCommandModule(MusicService musicService, PlaylistMessageStore playlistMessageStore)
    {
        _musicService = musicService;
        _playlistMessageStore = playlistMessageStore;
    }

    [SlashCommand("play", "Play a single track or add it to the queue if something is already playing.")]
    public async Task PlayAsync(string query)
    {
        var guild = Context.Guild;
        if (guild is null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message("This command can only be used in a guild."));
            return;
        }

        var voiceState = guild.VoiceStates.GetValueOrDefault(Context.User.Id);
        if (voiceState is null || voiceState.ChannelId is null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message("Please join a voice channel first."));
            return;
        }

        var guildId = guild.Id;
        var voiceChannelId = voiceState.ChannelId.Value;
        var interaction = Context.Interaction;

        await interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        try
        {
            var result = await _musicService.PlayAsync(guildId, voiceChannelId, query).ConfigureAwait(false);
            string content;
            if (result.AddedToQueue)
                content = $"Added to queue: **{result.Title}** ({result.QueuedCount} track(s) in queue)";
            else if (result.QueuedCount > 0)
                content = $"Now playing: **{result.Title}** ({result.QueuedCount} track(s) in queue)";
            else
                content = $"Now playing: **{result.Title}**";
            await interaction.SendFollowupMessageAsync(new InteractionMessageProperties { Content = content }).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("playlist", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                await interaction.SendFollowupMessageAsync(new InteractionMessageProperties
                {
                    Content = "A playlist is currently playing. Use /stop first."
                }).ConfigureAwait(false);
            }
            catch
            {
                // Ignore follow-up failures (e.g. interaction expired)
            }
        }
        catch (Exception ex)
        {
            try
            {
                await interaction.SendFollowupMessageAsync(new InteractionMessageProperties
                {
                    Content = $"Could not play: {ex.Message}"
                }).ConfigureAwait(false);
            }
            catch
            {
                // Ignore follow-up failures (e.g. interaction expired)
            }
        }
    }

    [SlashCommand("playlist", "Load and play a YouTube playlist. Replaces the current queue.")]
    public async Task PlaylistAsync(string url)
    {
        var guild = Context.Guild;
        if (guild is null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message("This command can only be used in a guild."));
            return;
        }

        var voiceState = guild.VoiceStates.GetValueOrDefault(Context.User.Id);
        if (voiceState is null || voiceState.ChannelId is null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message("Please join a voice channel first."));
            return;
        }

        var guildId = guild.Id;
        var voiceChannelId = voiceState.ChannelId.Value;
        var interaction = Context.Interaction;

        await interaction.SendResponseAsync(InteractionCallback.DeferredMessage());

        try
        {
            var result = await _musicService.PlayPlaylistAsync(guildId, voiceChannelId, url).ConfigureAwait(false);
            var embed = PlaylistEmbedBuilder.Build(result.Title ?? "Unknown", result.QueuedCount, _musicService.GetShuffle(guildId));
            var followUp = new InteractionMessageProperties
            {
                Embeds = new[] { embed },
                Components = new[] { PlaylistEmbedBuilder.CreateMusicControlsRow() }
            };
            var rest = Context.Client.Rest;
            var appId = Context.Interaction.ApplicationId;
            var token = Context.Interaction.Token;
            var channelId = Context.Channel.Id;
            var msg = await rest.SendInteractionFollowupMessageAsync(appId, token, followUp).ConfigureAwait(false);
            _playlistMessageStore.RegisterByGuild(guildId, channelId, msg.Id);
        }
        catch (Exception ex)
        {
            try
            {
                await interaction.SendFollowupMessageAsync(new InteractionMessageProperties
                {
                    Content = $"Could not load playlist: {ex.Message}"
                }).ConfigureAwait(false);
            }
            catch
            {
                // Ignore follow-up failures
            }
        }
    }

    [SlashCommand("skip", "Skip to the next track.")]
    public async Task SkipAsync()
    {
        var guild = Context.Guild;
        if (guild is null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message("This command can only be used in a guild."));
            return;
        }

        var title = await _musicService.SkipAsync(guild.Id).ConfigureAwait(false);
        if (title is null)
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message("No track in queue; playback stopped."));
        else
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message($"Now playing: **{title}**"));
    }

    [SlashCommand("previous", "Go back to the previous track.")]
    public async Task PreviousAsync()
    {
        var guild = Context.Guild;
        if (guild is null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message("This command can only be used in a guild."));
            return;
        }

        var title = await _musicService.PreviousAsync(guild.Id).ConfigureAwait(false);
        if (title is null)
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message("No previous track."));
        else
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message($"Now playing: **{title}**"));
    }

    [SlashCommand("stop", "Stop the current track and clear the queue.")]
    public async Task StopAsync()
    {
        var guild = Context.Guild;
        if (guild is null)
        {
            await Context.Interaction.SendResponseAsync(InteractionCallback.Message("This command can only be used in a guild."));
            return;
        }

        await _musicService.StopAsync(guild.Id);
        await Context.Interaction.SendResponseAsync(InteractionCallback.Message("Playback stopped."));
    }
}
