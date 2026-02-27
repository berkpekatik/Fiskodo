using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
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

        var rawTracks = loadResult.Data.Tracks;
        var encodedList = rawTracks
            .Where(t => !string.IsNullOrEmpty(t.Encoded))
            .Select(t => t.Encoded!)
            .ToList();        
        List<LavalinkTrackData>? decodedTracks = null;
        if (encodedList.Count > 0)
        {
            decodedTracks = await DecodeTracksAsync(encodedList, cancellationToken).ConfigureAwait(false);
            if (decodedTracks is null)
                decodedTracks = new List<LavalinkTrackData>();
        }

        var list = new List<LavalinkTrack>();
        var loadOptions = new TrackLoadOptions(SearchMode: TrackSearchMode.YouTube, StrictSearchBehavior.Passthrough);
        var decodedIndex = 0;
        foreach (var t in rawTracks)
        {
            LavalinkTrack? track = null;
            LavalinkTrackInfoData? fallbackInfo = null; // decodetracks'ten gelen info; fallback'te uri/identifier/title ile arama için            
            
            if (!string.IsNullOrEmpty(t.Encoded) && decodedTracks is not null && decodedIndex < decodedTracks.Count)
            {
                var decoded = decodedTracks[decodedIndex++];
                fallbackInfo = decoded.Info;
                track = CreateTrackFromEncoded(decoded.Encoded ?? t.Encoded!);
                if (track is not null && decoded.Info is not null)
                    ApplyTrackInfo(track, decoded.Info);
            }
            else if (!string.IsNullOrEmpty(t.Encoded))
            {
                track = CreateTrackFromEncoded(t.Encoded!);
                fallbackInfo = t.Info;
                if (track is not null && t.Info is not null)
                    ApplyTrackInfo(track, t.Info);
            }

            // Encoded ile track oluşturulamadıysa veya encoded yoksa: endpoint'ten gelen info veya loadtracks info ile uri/identifier/title'dan ara
            if (track is null)
            {
                var info = fallbackInfo ?? t.Info;
                if (!string.IsNullOrEmpty(info?.Uri))
                    track = await _audioService.Tracks
                        .LoadTrackAsync(info.Uri, loadOptions, resolutionScope: default, cancellationToken)
                        .ConfigureAwait(false);
                if (track is null && !string.IsNullOrEmpty(info?.Identifier))
                {
                    var ytUrl = "https://www.youtube.com/watch?v=" + info.Identifier;
                    track = await _audioService.Tracks
                        .LoadTrackAsync(ytUrl, loadOptions, resolutionScope: default, cancellationToken)
                        .ConfigureAwait(false);
                }
                if (track is null && !string.IsNullOrEmpty(info?.Title))
                {
                    var searchQuery = info.Title;
                    track = await _audioService.Tracks
                        .LoadTrackAsync(searchQuery, loadOptions, resolutionScope: default, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            if (track is not null)
                list.Add(track);
        }
        return list;
    }

    /// <summary>POST /v4/decodetracks — decode multiple encoded tracks in one request. Returns same order as request.</summary>
    private async Task<List<LavalinkTrackData>?> DecodeTracksAsync(List<string> encodedTracks, CancellationToken cancellationToken)
    {
        if (encodedTracks.Count == 0)
            return new List<LavalinkTrackData>();
        var url = $"{_lavalinkBaseAddress}/v4/decodetracks";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.TryAddWithoutValidation("Authorization", _lavalinkPassphrase);
        request.Content = JsonContent.Create(encodedTracks);
        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var list = await response.Content.ReadFromJsonAsync<List<LavalinkTrackData>>(options, cancellationToken).ConfigureAwait(false);
        return list;
    }

    /// <summary>Creates a LavalinkTrack from Lavalink's base64 encoded track data. Encoded is Lavaplayer binary format (not human-readable); we only pass it as-is to TrackData for play. Do not decode locally.</summary>
    private static LavalinkTrack? CreateTrackFromEncoded(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return null;
        var type = typeof(LavalinkTrack);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        // Lavalink4NET: CreateTrack sets TrackData = track.Data (the encoded base64). Use TrackData, not Identifier.
        try
        {
            var emptyCtor = type.GetConstructor(flags, Type.EmptyTypes);
            if (emptyCtor is not null)
            {
                var track = (LavalinkTrack)emptyCtor.Invoke(null);
                var trackDataProp = type.GetProperty("TrackData", flags);
                if (trackDataProp?.CanWrite == true)
                {
                    trackDataProp.SetValue(track, encoded);
                    return track;
                }
                var trackDataField = type.GetField("_trackData", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? type.GetField("<TrackData>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                if (trackDataField is not null)
                {
                    trackDataField.SetValue(track, encoded);
                    return track;
                }
            }
        }
        catch { /* ignore */ }

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

        return null;
    }

    /// <summary>Decodetracks/loadtracks info ile LavalinkTrack metadata (Title, Author, vb.) doldurur; "Unknown" yerine doğru bilgi görünür.</summary>
    private static void ApplyTrackInfo(LavalinkTrack track, LavalinkTrackInfoData info)
    {
        if (info is null) return;
        var type = typeof(LavalinkTrack);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        void Set(string name, object? value, Type? valueType = null)
        {
            if (value is null) return;
            var prop = type.GetProperty(name, flags);
            if (prop?.CanWrite == true)
            {
                var v = valueType is not null && value.GetType() != valueType ? Convert.ChangeType(value, valueType) : value;
                try { prop.SetValue(track, v); } catch { /* ignore */ }
                return;
            }
            var field = type.GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field is not null)
            {
                var v = valueType is not null && value.GetType() != valueType ? Convert.ChangeType(value, valueType) : value;
                try { field.SetValue(track, v); } catch { /* ignore */ }
            }
        }
        Set("Title", info.Title);
        Set("Author", info.Author);
        Set("Identifier", info.Identifier);
        Set("Uri", info.Uri);
        if (info.Length > 0)
            Set("Duration", TimeSpan.FromMilliseconds(info.Length), typeof(TimeSpan));
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

    /// <summary>Matches Lavalink /v4/decodetracks and loadtracks response: encoded + info (+ pluginInfo, userData ignored).</summary>
    private sealed class LavalinkTrackData
    {
        [System.Text.Json.Serialization.JsonPropertyName("encoded")]
        public string? Encoded { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("info")]
        public LavalinkTrackInfoData? Info { get; set; }
    }

    /// <summary>Matches Lavalink track info: identifier, title, author, length, uri, sourceName, artworkUrl, isrc, isSeekable, isStream, position.</summary>
    private sealed class LavalinkTrackInfoData
    {
        [System.Text.Json.Serialization.JsonPropertyName("identifier")]
        public string? Identifier { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string? Title { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("author")]
        public string? Author { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("length")]
        public long Length { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("uri")]
        public string? Uri { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("sourceName")]
        public string? SourceName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("artworkUrl")]
        public string? ArtworkUrl { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("isrc")]
        public string? Isrc { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("isSeekable")]
        public bool IsSeekable { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("isStream")]
        public bool IsStream { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("position")]
        public long Position { get; set; }
    }
}
