﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Jellyfin.Plugin.AnilistSync.API;
using Jellyfin.Plugin.AnilistSync.Configuration;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Model.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Dto;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.AnilistSync.Services
{

    public class PlaybackScrobbler : IServerEntryPoint
    {
        private readonly ISessionManager _sessionManager; // Needed to set up de startPlayBack and endPlayBack functions
        private readonly ILogger<PlaybackScrobbler> _logger;
        private readonly Dictionary<string, Guid> _lastScrobbled; // Library ID of last scrobbled item
        private readonly AnilistApi _anilistApi;
        private DateTime _nextTry;

        public PlaybackScrobbler(ISessionManager sessionManager, ILogger<PlaybackScrobbler> logger, AnilistApi anilistApi)
        {
            _sessionManager = sessionManager;
            _logger = logger;
            _anilistApi = anilistApi;
            _lastScrobbled = new Dictionary<string, Guid>();
            _nextTry = DateTime.UtcNow;
        }

        public Task RunAsync()
        {
            _sessionManager.PlaybackProgress += OnPlaybackProgress;
            _sessionManager.PlaybackStopped += OnPlaybackStopped;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

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
            // TODO: chan 5 to configurable value
            if (runtime < 60 * 10000 * userConfig.MinLength)
            {
                return false;
            }
            return true;
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


                // Send post request to API to update list
                int? episodes = (await _anilistApi.GetEpisodes(anilistId))?.Data?.Media?.Episodes;
                int? currentIndex = eventArgs.Item.IndexNumber;

                _logger.LogInformation("Total Episodes: " + episodes.ToString());
                _logger.LogInformation("Current Episode: " + currentIndex.ToString());

                MediaListStatus status = MediaListStatus.CURRENT;
                if (currentIndex == episodes)
                {
                    status = MediaListStatus.COMPLETED;
                }
                _logger.LogInformation(status.ToString());

                var response = await _anilistApi.PostListUpdate(anilistId, userConfig.UserToken, currentIndex, status);
                _logger.LogDebug("Scrobbled without errors");
                _lastScrobbled[eventArgs.Session.Id] = eventArgs.MediaInfo.Id;

            }
            //catch (InvalidTokenException)
            //{
            //    _logger.LogDebug("Deleted user token");
            //}
            catch (AnilistAPIException alEx)
            {
                for (int i = 0; i < alEx.errors?.Length; i++)
                {
                    Error? error = alEx.errors[i];
                    _logger.LogError(error.ErrorMessage, "API response code " + error.ErrorStatus);
                }
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