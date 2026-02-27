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
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _guildVoiceJoinedAt = new();

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
            _guildVoiceJoinedAt[guildId] = DateTimeOffset.UtcNow;
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

    /// <summary>Result of adding tracks as "next" in queue.</summary>
    public record AddAsNextResult(string FirstAddedTitle, int AddedCount, int TotalQueueCount);

    /// <summary>Loads tracks from a playlist URL or single track URL and inserts them as the next to play (after current). First added plays first, then the rest in order. If nothing is playing, starts playing the first added.</summary>
    public async Task<AddAsNextResult> AddTracksAsNextAsync(ulong guildId, ulong voiceChannelId, string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL must be provided.", nameof(url));

        var tracksToAdd = await LoadTracksFromUrlAsync(url, cancellationToken).ConfigureAwait(false);
        if (tracksToAdd.Count == 0)
            throw new InvalidOperationException("No tracks found or could not load.");

        var state = _guildQueues.GetOrAdd(guildId, _ => new GuildQueueState());
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);

        int addedCount = tracksToAdd.Count;
        string firstTitle = tracksToAdd[0].Title ?? "Unknown";
        int totalQueueCount;

        lock (state.Lock)
        {
            var existing = new List<LavalinkTrack>();
            while (state.Queue.TryDequeue(out var t))
                existing.Add(t);
            foreach (var t in tracksToAdd)
                state.Queue.Enqueue(t);
            foreach (var t in existing)
                state.Queue.Enqueue(t);
            totalQueueCount = state.Queue.Count;
        }

        bool nothingPlaying = player is null || (player.CurrentTrack is null && totalQueueCount == addedCount);
        if (nothingPlaying && player is null)
        {
            LavalinkTrack firstTrack;
            lock (state.Lock)
            {
                if (!state.Queue.TryDequeue(out firstTrack!))
                    throw new InvalidOperationException("No track to play.");
                totalQueueCount = state.Queue.Count;
            }
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
            _guildVoiceJoinedAt[guildId] = DateTimeOffset.UtcNow;
            state.IsPlaylistSession = true;
        }
        else if (nothingPlaying && player is not null && player.CurrentTrack is null)
        {
            LavalinkTrack firstTrack;
            lock (state.Lock)
            {
                if (!state.Queue.TryDequeue(out firstTrack!))
                    return new AddAsNextResult(firstTitle, addedCount, 0);
                totalQueueCount = state.Queue.Count;
            }
            await player.PlayAsync(firstTrack).ConfigureAwait(false);
            state.IsPlaylistSession = true;
        }

        return new AddAsNextResult(firstTitle, addedCount, totalQueueCount);
    }

    /// <summary>Loads one or more tracks from a playlist URL or single track URL. Returns empty list on failure.</summary>
    private async Task<List<LavalinkTrack>> LoadTracksFromUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        var identifier = ResolveQuery(url);
        if (IsPlaylistUrl(url))
            return await LoadPlaylistTracksAsync(identifier, cancellationToken).ConfigureAwait(false);
        var loadOptions = new TrackLoadOptions(SearchMode: TrackSearchMode.YouTube, StrictSearchBehavior.Passthrough);
        var track = await _audioService.Tracks
            .LoadTrackAsync(identifier, loadOptions, resolutionScope: default, cancellationToken)
            .ConfigureAwait(false);
        return track is null ? new List<LavalinkTrack>() : new List<LavalinkTrack> { track };
    }

    public async Task StopAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        _guildQueues.TryRemove(guildId, out _);
        _advanceLocks.TryRemove(guildId, out _);
        _guildVoiceJoinedAt.TryRemove(guildId, out _);
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

    /// <summary>Returns current track title, queue count, shuffle and artwork URL for playlist embed. Used when sending/updating the playlist message.</summary>
    public async Task<(string? NowPlayingTitle, int QueueCount, bool Shuffle, string? ArtworkUrl)> GetPlaylistEmbedInfoAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var player = await _audioService.Players
            .GetPlayerAsync<LavalinkPlayer>(guildId, cancellationToken)
            .ConfigureAwait(false);
        if (!_guildQueues.TryGetValue(guildId, out var state))
            return (null, 0, false, null);
        lock (state.Lock)
        {
            var title = player?.CurrentTrack?.Title ?? "Nothing";
            var artwork = player?.CurrentTrack?.ArtworkUri?.ToString();
            return (title, state.Queue.Count, state.Shuffle, artwork);
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

    /// <summary>When the bot joined this guild's voice channel (via this service). Null if not tracked.</summary>
    public DateTimeOffset? GetVoiceJoinedAt(ulong guildId) =>
        _guildVoiceJoinedAt.TryGetValue(guildId, out var at) ? at : null;

    /// <summary>Snapshot of a single queue item for API (title, optional author and duration in ms).</summary>
    public record QueueItemSnapshot(string Title, string? Author, long? DurationMs);

    /// <summary>Copy of the current queue for a guild (remaining tracks only). Empty if no queue state.</summary>
    public IReadOnlyList<QueueItemSnapshot> GetQueueSnapshot(ulong guildId)
    {
        if (!_guildQueues.TryGetValue(guildId, out var state))
            return Array.Empty<QueueItemSnapshot>();
        lock (state.Lock)
        {
            var list = new List<QueueItemSnapshot>(state.Queue.Count);
            foreach (var track in state.Queue)
                list.Add(GetTrackSnapshot(track));
            return list;
        }
    }

    private static QueueItemSnapshot GetTrackSnapshot(LavalinkTrack track)
    {
        var title = track.Title ?? "Unknown";
        string? author = null;
        long? durationMs = null;
        var type = typeof(LavalinkTrack);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var authorProp = type.GetProperty("Author", flags);
        if (authorProp?.CanRead == true)
        {
            try { author = authorProp.GetValue(track) as string; } catch { /* ignore */ }
        }
        var durationProp = type.GetProperty("Duration", flags) ?? type.GetProperty("Length", flags);
        if (durationProp?.CanRead == true)
        {
            try
            {
                var val = durationProp.GetValue(track);
                if (val is TimeSpan ts)
                    durationMs = (long)ts.TotalMilliseconds;
                else if (val is long l)
                    durationMs = l;
            }
            catch { /* ignore */ }
        }
        return new QueueItemSnapshot(title, author, durationMs);
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
        var list = new List<LavalinkTrack>();
        var loadOptions = new TrackLoadOptions(SearchMode: TrackSearchMode.YouTube, StrictSearchBehavior.Passthrough);
        foreach (var t in rawTracks)
        {
            LavalinkTrack? track = null;
            // Encoded varsa: direkt TrackData'ya yaz ve loadtracks'in info'su ile metadata doldur.
            if (!string.IsNullOrEmpty(t.Encoded))
            {
                track = CreateTrackFromEncoded(t.Encoded!);
                if (track is not null && t.Info is not null)
                    ApplyTrackInfo(track, t.Info);
            }

            // Encoded ile track oluşturulamadıysa veya encoded yoksa: loadtracks info ile uri/identifier/title'dan ara
            if (track is null)
            {
                var info = t.Info;
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

    /// <summary>LavalinkTrackInfoData (loadtracks rawTracks.Info) içeriğini LavalinkTrack'e yazar. Property isimleri farklı olabilir (örn. ArtworkUrl→ArtworkUri, Length→Duration); birden fazla isim denenir.</summary>
    private static void ApplyTrackInfo(LavalinkTrack track, LavalinkTrackInfoData info)
    {
        if (info is null) return;
        var type = typeof(LavalinkTrack);
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        void SetValue(string propertyName, object? value, Type? valueType = null)
        {
            if (value is null) return;
            var v = valueType is not null && value.GetType() != valueType ? Convert.ChangeType(value, valueType) : value;
            var prop = type.GetProperty(propertyName, flags);
            if (prop?.CanWrite == true)
            {
                try { prop.SetValue(track, v); } catch { /* ignore */ }
                return;
            }
            var field = type.GetField("<" + propertyName + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field is not null)
                try { field.SetValue(track, v); } catch { /* ignore */ }
        }

        void SetOneOf(string[] propertyNames, object? value, Type? valueType = null)
        {
            if (value is null) return;
            var v = valueType is not null && value.GetType() != valueType ? Convert.ChangeType(value, valueType) : value;
            foreach (var name in propertyNames)
            {
                var prop = type.GetProperty(name, flags);
                if (prop?.CanWrite == true)
                {
                    try { prop.SetValue(track, v); } catch { /* ignore */ }
                    return;
                }
                var field = type.GetField("<" + name + ">k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field is not null)
                {
                    try { field.SetValue(track, v); } catch { /* ignore */ }
                    return;
                }
            }
        }

        SetValue("Title", info.Title);
        SetValue("Author", info.Author);
        SetValue("Identifier", info.Identifier);
        SetValue("Uri", info.Uri);
        if (info.Length > 0)
            SetOneOf(new[] { "Duration", "Length" }, TimeSpan.FromMilliseconds(info.Length), typeof(TimeSpan));
        if (!string.IsNullOrEmpty(info.ArtworkUrl))
        {
            if (Uri.TryCreate(info.ArtworkUrl, UriKind.Absolute, out var artworkUri))
                SetOneOf(new[] { "ArtworkUri", "ArtworkUrl" }, artworkUri);
            SetOneOf(new[] { "ArtworkUri", "ArtworkUrl" }, info.ArtworkUrl);
        }
        SetOneOf(new[] { "SourceName", "Source" }, info.SourceName);
        SetValue("IsSeekable", info.IsSeekable);
        SetValue("IsLiveStream", info.IsStream);
        SetOneOf(new[] { "StartPosition", "Position" }, info.Position > 0 ? TimeSpan.FromMilliseconds(info.Position) : null, typeof(TimeSpan));
        SetValue("Isrc", info.Isrc);
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
