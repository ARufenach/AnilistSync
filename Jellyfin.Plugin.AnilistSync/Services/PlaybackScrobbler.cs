using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.AnilistSync.API;
using Jellyfin.Plugin.AnilistSync.API.Exceptions;
using Jellyfin.Plugin.AnilistSync.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AnilistSync.Services
{
    /// <summary>
    /// Playback progress scrobbler.
    /// </summary>
    public class PlaybackScrobbler : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager; // Needed to set up the startPlayBack and endPlayBack functions
        private readonly ILogger<PlaybackScrobbler> _logger;
        private readonly Dictionary<string, Guid> _lastScrobbled; // Library ID of last scrobbled item
        private readonly AnilistApi _anilistApi;
        private DateTime _nextTry;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaybackScrobbler"/> class.
        /// </summary>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{PlaybackScrobbler}"/> interface.</param>
        /// <param name="anilistApi">Instance of the <see cref="AnilistApi"/>.</param>
        public PlaybackScrobbler(ISessionManager sessionManager, ILogger<PlaybackScrobbler> logger, AnilistApi anilistApi)
        {
            _sessionManager = sessionManager;
            _logger = logger;
            _anilistApi = anilistApi;
            _lastScrobbled = new Dictionary<string, Guid>();
            _nextTry = DateTime.UtcNow;
        }

        /// <inheritdoc/>
        public Task RunAsync()
        {
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose.
        /// </summary>
        /// <param name="disposing">Dispoe all resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _sessionManager.PlaybackProgress -= OnPlaybackProgress;
                _sessionManager.PlaybackStopped -= OnPlaybackStopped;
            }
        }

        // UserConfig config, 
        private static bool CanBeScrobbled(UserConfig userConfig, PlaybackProgressEventArgs playbackProgress)
        {
            var position = playbackProgress.PlaybackPositionTicks;
            var runtime = playbackProgress.MediaInfo.RunTimeTicks;

            if (runtime != null)
            {
                var percentageWatched = position / (float)runtime * 100f;

                // Check if percentageWatched is greater than threshold
                if (percentageWatched < userConfig.ScrobblePercentage)
                {
                    return false;
                }
            }

            // Checks if runtime is greater than min length to be scrobbled
            if (runtime < 60 * 10000 * userConfig.MinLength)
            {
                return false;
            }

            // Check if movie or episode then check against user config
            return playbackProgress.MediaInfo.Type switch
            {
                BaseItemKind.Movie => userConfig.ScrobbleMovies,
                BaseItemKind.Episode => userConfig.ScrobbleShows,
                _ => false
            };
        }

        private async void OnPlaybackProgress(object? sessions, PlaybackProgressEventArgs eventArgs)
        {
            if (DateTime.UtcNow < _nextTry)
            {
                return;
            }

            // Scrobble every 30s
            _nextTry = DateTime.UtcNow.AddSeconds(30);
            await ScrobbleSession(eventArgs);
        }

        private async void OnPlaybackStopped(object? sessions, PlaybackStopEventArgs eventArgs)
        {
            await ScrobbleSession(eventArgs);
        }

        private static string? GetAnilistId(PlaybackProgressEventArgs eventArgs)
        {
            string? id = null;
            if (eventArgs.Item is Episode episode)
            {
                id = episode.Series.GetProviderId("AniList");
            }
            else if (eventArgs.Item is Movie movie)
            {
                id = movie.GetProviderId("AniList");
            }
            return id;
        }

        private async Task ScrobbleSession(PlaybackProgressEventArgs eventArgs)
        {
            try
            {
                var userId = eventArgs.Session.UserId;

                //Get user config
                var userConfig = Plugin.Instance?.Configuration.GetByGuid(userId);

                // Check if logged in
                if (userConfig == null || string.IsNullOrEmpty(userConfig.UserToken))
                {
                    _logger.LogError(
                        "Can't scrobble: User {UserName} not logged in ({UserConfigStatus})",
                        eventArgs.Session.UserName,
                        userConfig == null);
                    return;
                }

                // Scrobble code
                if (!CanBeScrobbled(userConfig, eventArgs))
                {
                    return;
                }

                // Check if already scrobbled
                if (_lastScrobbled.ContainsKey(eventArgs.Session.Id) && _lastScrobbled[eventArgs.Session.Id] == eventArgs.MediaInfo.Id)
                {
                    _logger.LogDebug("Already scrobbled {ItemName} for {UserName}", eventArgs.MediaInfo.Name, eventArgs.Session.UserName);
                    return;
                }

                // Get AniList Id and check if exists in Jellyfin
                string? anilistId = GetAnilistId(eventArgs);
                if (anilistId == null)
                {
                    _logger.LogDebug("Cannot Scrobble {ItemName}, unknown AniList Id.");
                    return;
                }

                _logger.LogInformation(
                    "Trying to scrobble {Name} ({NowPlayingId}) for {UserName} ({UserId}) - {PlayingItemPath} on {SessionId} - AniList ID {AnilistId}",
                    eventArgs.MediaInfo.Name,
                    eventArgs.MediaInfo.Id,
                    eventArgs.Session.UserName,
                    userId,
                    eventArgs.MediaInfo.Path,
                    eventArgs.Session.Id,
                    anilistId);


                // Get the list entry for current watching show from Anilist
                var listEntry = _anilistApi.GetListEntry(userConfig.UserToken, Int32.Parse(anilistId)).Result?.Data?.ListEntry;

                int? currentIndex = eventArgs.Item.IndexNumber;
                int? currentRemoteIndex = listEntry?.Progress;
                MediaListStatus? status = listEntry?.Status;

                switch (status)
                {
                    case MediaListStatus.COMPLETED: // Check if STARTING a rewatch
                        if (userConfig.ScrobbleRewatches)
                        {
                            if (currentIndex == 1) // Only initialize a rewatch if watching first episode
                            {
                                status = MediaListStatus.REPEATING;
                                await _anilistApi.PostListStatusUpdate(userConfig.UserToken, listEntry?.Id, status);
                                _logger.LogInformation("Rewatch started");
                            }
                            else
                            {
                                _logger.LogInformation("Attempting to start rewatch from middle episode, discarding scrobble");
                                return;
                            }
                        }
                        else
                        {
                            _logger.LogInformation("User has chosen not to scrobble rewatches");
                            return;
                        }
                        break;
                    case MediaListStatus.REPEATING:
                        if (userConfig.ScrobbleRewatches)
                        {
                            if (currentIndex <= currentRemoteIndex)
                            {
                                _logger.LogInformation("Episode number <= Anilist episode watch count, discarding scrobble");
                                return;
                            }
                        }
                        else
                        {
                            _logger.LogInformation("User has chosen not to scrobble rewatches");
                            return;
                        }
                        break;
                    case MediaListStatus.PLANNING:
                    case MediaListStatus.DROPPED:
                    case MediaListStatus.PAUSED:
                        status = MediaListStatus.CURRENT;
                        await _anilistApi.PostListStatusUpdate(userConfig.UserToken, listEntry?.Id, status);
                        break;
                    case MediaListStatus.CURRENT:
                        if (currentIndex <= currentRemoteIndex)
                        {
                            _logger.LogInformation("Episode number <= Anilist episode watch count, discarding scrobble");
                            return;
                        }
                        break;
                    default:
                        break;
                }

                // Get total number of episodes of current item from Anilist
                int? totalEpisodes = (await _anilistApi.GetEpisodes(Int32.Parse(anilistId)))?.Data?.Media?.Episodes;

                // If watching LAST episode change status to completed
                if (currentIndex == totalEpisodes)
                {
                    status = MediaListStatus.COMPLETED;
                    await _anilistApi.PostListStatusUpdate(userConfig.UserToken, listEntry?.Id, status);
                }
                else
                {
                    await _anilistApi.PostListProgressUpdate(userConfig.UserToken, listEntry?.Id, currentIndex);
                }
                _logger.LogInformation("Scrobbled episode: ({currentIndex} of {totalEpisodes}) for Anilist ID: ({anilistId})", currentIndex, totalEpisodes, anilistId);
                _logger.LogInformation("Watch status: {status}", status);
                _logger.LogInformation("Scrobbled without errors");
                _lastScrobbled[eventArgs.Session.Id] = eventArgs.MediaInfo.Id;

            }
            catch (AnilistAPIException aniAPIEx)
            {
                _logger.LogError(aniAPIEx, "Error status: {aniAPIEx.statusCode}", aniAPIEx.statusCode);
            }
            catch (InvalidDataException ex)
            {
                _logger.LogError(ex, "Couldn't scrobble");
                _lastScrobbled[eventArgs.Session.Id] = eventArgs.MediaInfo.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Caught unknown exception while trying to scrobble");
            }
        }
    }
}