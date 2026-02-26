using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Lavalink4NET;
using Lavalink4NET.Players;
using Lavalink4NET.Players.Queued;
using Lavalink4NET.Rest.Entities.Tracks;
using Lavalink4NET.Tracks;
using Microsoft.Extensions.Options;

namespace Fiskodo.Services;

public sealed class MusicService
{
    private readonly IAudioService _audioService;
    private readonly PlaylistMessageStore? _playlistMessageStore;
    private readonly string _lavalinkBaseAddress;
    private readonly string _lavalinkPassphrase;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<ulong, GuildQueueState> _guildQueues = new();
    private readonly ConcurrentDictionary<ulong, object> _advanceLocks = new();

    public MusicService(IAudioService audioService, IConfiguration configuration, IHttpClientFactory httpClientFactory, PlaylistMessageStore? playlistMessageStore = null)
    {
        _audioService = audioService;
        _playlistMessageStore = playlistMessageStore;
        var section = configuration.GetSection("Lavalink");
        _lavalinkBaseAddress = (section["BaseAddress"] ?? "http://localhost:2333").TrimEnd('/');
        _lavalinkPassphrase = section["Passphrase"] ?? "youshallnotpass";
        _httpClient = httpClientFactory.CreateClient();
    }

    /// <summary>Result of a play operation: title, queue count, and whether the track was only added to queue.</summary>
    public record PlayResult(string Title, int QueuedCount, bool AddedToQueue = false);

    /// <summary>True if the guild is currently in a playlist session (started via /playlist).</summary>
    public bool IsPlaylistSession(ulong guildId)
    {
        return _guildQueues.TryGetValue(guildId, out var state) && state.IsPlaylistSession;
    }

    /// <summary>Plays a single track or adds it to the queue if something is already playing. Never loads a playlist.</summary>
    public async Task<PlayResult> PlayAsync(ulong guildId, ulong voiceChannelId, string queryOrUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryOrUrl))
            throw new ArgumentException("Query or URL must be provided.", nameof(queryOrUrl));

        if (_guildQueues.TryGetValue(guildId, out var existingState) && existingState.IsPlaylistSession)
            throw new InvalidOperationException("A playlist is currently playing. Use /stop first.");

        var loadIdentifier = ResolveQuery(queryOrUrl);
        var loadOptions = new TrackLoadOptions(SearchMode: TrackSearchMode.YouTube, StrictSearchBehavior.Passthrough);
        var track = await _audioService.Tracks
            .LoadTrackAsync(loadIdentifier, loadOptions, resolutionScope: default, cancellationToken)
            .ConfigureAwait(false);
        if (track is null)
            throw new InvalidOperationException("No matches found for your query or the source is not available.");

        var state = _guildQueues.GetOrAdd(guildId, _ => new GuildQueueState());
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);

        bool queueActive;
        lock (state.Lock)
        {
            queueActive = player is not null && (player.CurrentTrack is not null || state.Queue.Count > 0);
        }

        if (queueActive)
        {
            lock (state.Lock)
            {
                state.Queue.Enqueue(track);
            }
            return new PlayResult(track.Title ?? "Unknown", state.Queue.Count, AddedToQueue: true);
        }

        lock (state.Lock)
        {
            state.Queue.Enqueue(track);
        }

        LavalinkTrack firstTrack;
        int queuedCount;
        lock (state.Lock)
        {
            if (!state.Queue.TryDequeue(out firstTrack!))
                throw new InvalidOperationException("No track to play.");
            queuedCount = state.Queue.Count;
        }

        if (player is null)
        {
            var playerOptions = new LavalinkPlayerOptions
            {
                InitialTrack = new TrackQueueItem(firstTrack),
            };
            await _audioService.Players
                .JoinAsync<LavalinkPlayer, LavalinkPlayerOptions>(
                    guildId,
                    voiceChannelId,
                    PlayerFactory.Default,
                    Options.Create(playerOptions),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await player.PlayAsync(firstTrack).ConfigureAwait(false);
        }

        lock (state.Lock)
        {
            state.PreviousHistory.Clear();
        }

        return new PlayResult(firstTrack.Title ?? "Unknown", queuedCount, AddedToQueue: false);
    }

    /// <summary>Loads a playlist from the given URL and starts playing it. Replaces any current queue. Use only with playlist URLs.</summary>
    public async Task<PlayResult> PlayPlaylistAsync(ulong guildId, ulong voiceChannelId, string playlistUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(playlistUrl))
            throw new ArgumentException("Playlist URL must be provided.", nameof(playlistUrl));
        if (!IsPlaylistUrl(playlistUrl))
            throw new InvalidOperationException("Not a playlist URL. Use /play for a single track, or provide a YouTube playlist link (e.g. with list=).");

        var loadIdentifier = ResolveQuery(playlistUrl);
        var tracksToPlay = await LoadPlaylistTracksAsync(loadIdentifier, cancellationToken).ConfigureAwait(false);
        if (tracksToPlay.Count == 0)
            throw new InvalidOperationException("Playlist is empty or could not be loaded.");

        var state = _guildQueues.GetOrAdd(guildId, _ => new GuildQueueState());
        lock (state.Lock)
        {
            state.IsPlaylistSession = true;
            state.Queue.Clear();
            state.PreviousHistory.Clear();
            foreach (var t in tracksToPlay)
                state.Queue.Enqueue(t);
        }

        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);

        LavalinkTrack firstTrack;
        int queuedCount;
        lock (state.Lock)
        {
            if (!state.Queue.TryDequeue(out firstTrack!))
                throw new InvalidOperationException("No track to play.");
            queuedCount = state.Queue.Count;
        }

        if (player is null)
        {
            var playerOptions = new LavalinkPlayerOptions
            {
                InitialTrack = new TrackQueueItem(firstTrack),
            };
            await _audioService.Players
                .JoinAsync<LavalinkPlayer, LavalinkPlayerOptions>(
                    guildId,
                    voiceChannelId,
                    PlayerFactory.Default,
                    Options.Create(playerOptions),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await player.PlayAsync(firstTrack).ConfigureAwait(false);
        }

        lock (state.Lock)
        {
            state.PreviousHistory.Clear();
        }

        return new PlayResult(firstTrack.Title ?? "Unknown", queuedCount, AddedToQueue: false);
    }

    public async Task StopAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        _guildQueues.TryRemove(guildId, out _);
        _advanceLocks.TryRemove(guildId, out _);
        _playlistMessageStore?.UnregisterByGuild(guildId);
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);
        if (player is null)
            return;
        await player.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<string?> SkipAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);
        if (player is null)
            return null;

        var state = _guildQueues.GetOrAdd(guildId, _ => new GuildQueueState());
        LavalinkTrack? next;
        lock (state.Lock)
        {
            if (player.CurrentTrack is not null)
            {
                state.PreviousHistory.Insert(0, player.CurrentTrack);
                if (state.PreviousHistory.Count > 2)
                    state.PreviousHistory.RemoveAt(state.PreviousHistory.Count - 1);
            }
            if (state.Shuffle && state.Queue.Count > 1)
            {
                var list = state.Queue.ToList();
                Shuffle(list);
                state.Queue.Clear();
                foreach (var t in list)
                    state.Queue.Enqueue(t);
            }
            if (!state.Queue.TryDequeue(out next))
                next = null;
        }
        if (next is null)
        {
            await player.StopAsync(cancellationToken).ConfigureAwait(false);
            return null;
        }
        await player.PlayAsync(next).ConfigureAwait(false);
        return next.Title ?? "Unknown";
    }

    public async Task<string?> PreviousAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);
        if (player is null)
            return null;

        var state = _guildQueues.GetOrAdd(guildId, _ => new GuildQueueState());
        LavalinkTrack? toPlay;
        lock (state.Lock)
        {
            if (state.PreviousHistory.Count == 0)
                return null;
            toPlay = state.PreviousHistory[0];
            state.PreviousHistory.RemoveAt(0);
            if (player.CurrentTrack is not null)
                state.Queue.Enqueue(player.CurrentTrack);
        }

        await player.PlayAsync(toPlay).ConfigureAwait(false);
        return toPlay.Title ?? "Unknown";
    }

    public void ToggleShuffle(ulong guildId)
    {
        var state = _guildQueues.GetOrAdd(guildId, _ => new GuildQueueState());
        lock (state.Lock)
        {
            state.Shuffle = !state.Shuffle;
            if (state.Shuffle && state.Queue.Count > 1)
            {
                var list = state.Queue.ToList();
                Shuffle(list);
                state.Queue.Clear();
                foreach (var t in list)
                    state.Queue.Enqueue(t);
            }
        }
    }

    public bool GetShuffle(ulong guildId)
    {
        if (!_guildQueues.TryGetValue(guildId, out var state))
            return false;
        lock (state.Lock)
            return state.Shuffle;
    }

    /// <summary>True if the bot has nothing playing and no tracks in queue for this guild. Used by voice cleanup (auto-leave when idle).</summary>
    public async Task<bool> IsIdleAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);
        if (player is null)
            return true;
        if (player.CurrentTrack is not null)
            return false;
        if (!_guildQueues.TryGetValue(guildId, out var state))
            return true;
        lock (state.Lock)
            return state.Queue.Count == 0;
    }

    /// <summary>Guild IDs that have queue state (active play or playlist). Used by the advance service to check for auto-next.</summary>
    public IReadOnlyList<ulong> GetGuildIdsWithQueue()
    {
        return _guildQueues.Keys.ToList();
    }

    /// <summary>When the current track has ended (player has no current track) and queue has items, plays the next track. Returns true if it advanced. Uses a per-guild lock to prevent double advance.</summary>
    public async Task<bool> TryAdvanceToNextAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var lockObj = _advanceLocks.GetOrAdd(guildId, _ => new object());
        lock (lockObj)
        {
            if (!_guildQueues.TryGetValue(guildId, out _))
                return false;
        }
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);
        if (player is null || !_guildQueues.TryGetValue(guildId, out var state))
            return false;

        LavalinkTrack? next;
        lock (lockObj)
        {
            if (player.CurrentTrack is not null)
                return false;
            lock (state.Lock)
            {
                if (state.Queue.Count == 0)
                    return false;
                if (state.Shuffle && state.Queue.Count > 1)
                {
                    var list = state.Queue.ToList();
                    Shuffle(list);
                    state.Queue.Clear();
                    foreach (var t in list)
                        state.Queue.Enqueue(t);
                }
                if (!state.Queue.TryDequeue(out next))
                    return false;
            }
        }
        await player.PlayAsync(next).ConfigureAwait(false);
        return true;
    }

    /// <summary>Returns current track title, queue count and shuffle for playlist embed. Used when updating the playlist message.</summary>
    public async Task<(string? NowPlayingTitle, int QueueCount, bool Shuffle)> GetPlaylistEmbedInfoAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);
        if (!_guildQueues.TryGetValue(guildId, out var state))
            return (null, 0, false);
        lock (state.Lock)
        {
            var title = player?.CurrentTrack?.Title ?? "Nothing";
            return (title, state.Queue.Count, state.Shuffle);
        }
    }

    /// <summary>Playback status for a guild (for status API). Null title and 0 queue = idle.</summary>
    public async Task<(string? NowPlayingTitle, int QueueCount, bool IsPlaylistSession, bool Shuffle)> GetPlaybackStatusAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);
        if (!_guildQueues.TryGetValue(guildId, out var state))
            return (player?.CurrentTrack?.Title, 0, false, false);
        lock (state.Lock)
        {
            var title = player?.CurrentTrack?.Title;
            return (title, state.Queue.Count, state.IsPlaylistSession, state.Shuffle);
        }
    }

    private async Task<List<LavalinkTrack>> LoadPlaylistTracksAsync(string identifier, CancellationToken cancellationToken)
    {
        var url = $"{_lavalinkBaseAddress}/v4/loadtracks?identifier={Uri.EscapeDataString(identifier)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", _lavalinkPassphrase);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var loadResult = JsonSerializer.Deserialize<LavalinkLoadResult>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (loadResult?.Data?.Tracks is null || loadResult.Data.Tracks.Count == 0)
            return new List<LavalinkTrack>();

        var list = new List<LavalinkTrack>();
        var loadOptions = new TrackLoadOptions(SearchMode: TrackSearchMode.YouTube, StrictSearchBehavior.Passthrough);
        foreach (var t in loadResult.Data.Tracks)
        {
            LavalinkTrack? track = null;
            if (!string.IsNullOrEmpty(t.Info?.Uri))
            {
                track = await _audioService.Tracks
                    .LoadTrackAsync(t.Info.Uri, loadOptions, resolutionScope: default, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (track is null && !string.IsNullOrEmpty(t.Info?.Identifier))
            {
                var ytUrl = "https://www.youtube.com/watch?v=" + t.Info.Identifier;
                track = await _audioService.Tracks
                    .LoadTrackAsync(ytUrl, loadOptions, resolutionScope: default, cancellationToken)
                    .ConfigureAwait(false);
            }
            if (track is null && !string.IsNullOrEmpty(t.Encoded))
            {
                track = CreateTrackFromEncoded(t.Encoded);
            }
            if (track is not null)
                list.Add(track);
        }
        return list;
    }

    private static LavalinkTrack? CreateTrackFromEncoded(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return null;
        var type = typeof(LavalinkTrack);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        try
        {
            var ctor = type.GetConstructor(flags, new[] { typeof(string) });
            if (ctor is not null)
                return (LavalinkTrack)ctor.Invoke(new object[] { encoded });
        }
        catch { /* ignore */ }

        try
        {
            var decode = type.GetMethod("Decode", flags, new[] { typeof(string) });
            if (decode is not null && decode.IsStatic && decode.ReturnType == type)
                return (LavalinkTrack?)decode.Invoke(null, new object[] { encoded });
        }
        catch { /* ignore */ }

        try
        {
            var emptyCtor = type.GetConstructor(flags, Type.EmptyTypes);
            if (emptyCtor is not null)
            {
                var track = (LavalinkTrack)emptyCtor.Invoke(null);
                var prop = type.GetProperty("Identifier", flags);
                if (prop?.CanWrite == true)
                {
                    prop.SetValue(track, encoded);
                    return track;
                }
                var field = type.GetField("_identifier", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? type.GetField("<Identifier>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field is not null)
                {
                    field.SetValue(track, encoded);
                    return track;
                }
            }
        }
        catch { /* ignore */ }

        return null;
    }

    private static bool IsPlaylistUrl(string queryOrUrl)
    {
        if (string.IsNullOrWhiteSpace(queryOrUrl))
            return false;
        return queryOrUrl.Contains("list=", StringComparison.OrdinalIgnoreCase) ||
               queryOrUrl.Contains("playlist", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveQuery(string queryOrUrl)
    {
        if (Uri.TryCreate(queryOrUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            return queryOrUrl;
        return queryOrUrl;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        var r = Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = r.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private sealed class GuildQueueState
    {
        public readonly object Lock = new();
        public readonly Queue<LavalinkTrack> Queue = new();
        /// <summary>Most recent previous at index 0; max 2 elements.</summary>
        public readonly List<LavalinkTrack> PreviousHistory = new();
        public bool Shuffle;
        public bool IsPlaylistSession;
    }

    private sealed class LavalinkLoadResult
    {
        public string? LoadType { get; set; }
        public LavalinkLoadResultData? Data { get; set; }
    }

    private sealed class LavalinkLoadResultData
    {
        public List<LavalinkTrackData>? Tracks { get; set; }
    }

    private sealed class LavalinkTrackData
    {
        [System.Text.Json.Serialization.JsonPropertyName("encoded")]
        public string? Encoded { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("info")]
        public LavalinkTrackInfoData? Info { get; set; }
    }

    private sealed class LavalinkTrackInfoData
    {
        [System.Text.Json.Serialization.JsonPropertyName("uri")]
        public string? Uri { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("identifier")]
        public string? Identifier { get; set; }
    }
}
