using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using Microsoft.Extensions.Logging;
using Jellyfin.Plugin.AnilistSync.API.Exceptions;

namespace Jellyfin.Plugin.AnilistSync.API
{
    /// <summary>
    /// Anilist API.
    /// </summary>
    public class AnilistApi
    {
        // Interfaces //
        private readonly ILogger<AnilistApi> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly JsonSerializerOptions _jsonSerializerOptions;

        // Base URLs // 

        /// <summary>
        /// Base url for OAUth uses.
        /// </summary>
        public const string BaseOauthUrl = @"https://anilist.co/api/v2";

        /// <summary>
        /// Base url for GraphQL queries.
        /// </summary>
        public const string GraphQLUrl = @"https://graphql.anilist.co";

        /// <summary>
        /// Generic GraphQL query string used for list updates.
        /// </summary>
        public const string QueryString = @"
mutation ($id: Int, $mediaId: Int, $status: MediaListStatus, $progress: Int,) { 
  SaveMediaListEntry(id: $id, mediaId: $mediaId, status: $status, progress: $progress ) { 
    id,
    mediaId,
    status,
    progress
  }
}";
        /// <summary>
        /// Anilist client ID.
        /// </summary>
        public const int ClientId = 5659;

        /// <summary>
        /// Secret.
        /// </summary>
        public const string Secret = @"h7ym2GZ6OjrdJ9sygDP7kDnQWsBdTwp4U8s7pt4X";

        /// <summary>
        /// Initializes a new instance of <see cref="AnilistApi"/> class.
        /// </summary>
        /// <param name="logger">Instance of the <see cref="ILogger{AnilistApi}"/> interface.</param>
        /// <param name="httpClientFactory">Instance of the <see cref="IHttpClientFactory"/> interface.</param>
        public AnilistApi(ILogger<AnilistApi> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _jsonSerializerOptions = JsonDefaults.Options;
        }

        /// <summary>
        /// Get token.
        /// </summary>
        /// <param name="code">code.</param>
        /// <returns><see cref="CodeResponse"/></returns>
        public async Task<CodeResponse?> GetToken(string? code)
        {
            string uri = @"/oauth/token";
            string payload = JsonSerializer.Serialize(new OAuth
            {
                GrantType = "authorization_code",
                ClientId = ClientId,
                ClientSecret = Secret,
                RedirectUri = $"{ BaseOauthUrl }/oauth/pin",
                Code = code
            });
            using HttpContent content = new StringContent(payload, Encoding.UTF8, "application/json");
            var responseMessage = await _httpClientFactory.CreateClient(NamedClient.Default).PostAsync(BaseOauthUrl + uri, content);
            return await responseMessage.Content.ReadFromJsonAsync<CodeResponse>(_jsonSerializerOptions);
        }

        /// <summary>
        /// Gets total episodes of specified Anilist mediaID.
        /// </summary>
        /// <param name="anilistId">Anilist ID.</param>
        /// <returns><see cref="RootObject"/> containing total episodes parameter.</returns>
        public async Task<RootObject?> GetEpisodes(int? anilistId)
        {
            GraphQLBody content = new GraphQLBody { 
                Query = @"query ($mediaId: Int) { Media (id: $mediaId) { episodes }}",
                Variables = new ListEntry
                {
                    MediaId = anilistId,
                    Id = null,
                    Progress = null,
                    Status = null
                }
            };
            return await Post(content);
        }

        /// <summary>
        /// Gets the name and ID of the currently authenticated user.
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <returns><see cref="RootObject"/>containing <see cref="User"/> object.</returns>
        public async Task<RootObject?> GetUser(string? userToken)
        {
            GraphQLBody content = new GraphQLBody { 
                Query = @"query { Viewer { id, name }}",
                Variables = null
            };
            return await PostWithAuth(userToken, content);
        }

        /// <summary>
        /// Gets user's list ID of specified Anilist mediaID
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <param name="anilistId">Anilist mediaID</param>
        /// <returns><see cref="RootObject"/> containing <see cref="ListEntry"/></returns>
        public async Task<RootObject?> GetListEntry(string? userToken, int? anilistId)
        {
            GraphQLBody content = new GraphQLBody
            {
                Query = QueryString,
                Variables = new ListEntry
                {
                    MediaId = anilistId,
                    Id = null,
                    Progress = null,
                    Status = null
                }
            };
            return await PostWithAuth(userToken, content);
        }

        /// <summary>
        /// Updates status of specified list item
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <param name="listId">List ID</param>
        /// <param name="status">Status.</param>
        /// <returns><see cref="RootObject"/> containing updated list item.</returns>
        public async Task<RootObject?> PostListStatusUpdate(string? userToken, int? listId, MediaListStatus? status)
        {
            GraphQLBody content = new GraphQLBody
            {
                Query = QueryString,
                Variables = new ListEntry
                {
                    MediaId = null,
                    Id = listId,
                    Progress = null,
                    Status = status
                }
            };
            return await PostWithAuth(userToken, content);
        }

        /// <summary>
        /// Updates progress of specified list item.
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <param name="listId">List ID></param>
        /// <param name="progress">Progress.</param>
        /// <returns><see cref="RootObject"/> containing updated list item.</returns>
        public async Task<RootObject?> PostListProgressUpdate(string? userToken, int? listId, int? progress)
        {
            GraphQLBody content = new GraphQLBody
            {
                Query = QueryString,
                Variables = new ListEntry
                {
                    MediaId = null,
                    Id = listId,
                    Progress = progress,
                    Status = null
                }
            };
            return await PostWithAuth(userToken, content);
        }

        /// <summary>
        /// API private GraphQL Post WITHOUT authentication.
        /// </summary>
        /// <param name="graphQL"><see cref="GraphQLBody"/> containing query and variables</param>
        /// <returns><see cref="RootObject"/> containing API response.</returns>
        public async Task<RootObject?> Post(GraphQLBody graphQL)
        {
            var responseMessage = await _httpClientFactory.CreateClient(NamedClient.Default).PostAsJsonAsync<GraphQLBody>(GraphQLUrl, graphQL, _jsonSerializerOptions);
            var root = await responseMessage.Content.ReadFromJsonAsync<RootObject>(_jsonSerializerOptions);
            if (root?.Errors != null)
            {
                foreach (AnilistError error in root.Errors)
                {
                    throw new AnilistAPIException(error.ErrorMessage, error.ErrorStatus, error.Locations);
                }
            }
            return root;
        }

        /// <summary>
        /// API private GraphQL Post WITH authentication.
        /// </summary>
        /// <param name="userToken">User token.</param>
        /// <param name="graphQL"><see cref="GraphQLBody"/> containing query and variables</param>
        /// <returns><see cref="RootObject"/> containing API response.</returns>
        public async Task<RootObject?> PostWithAuth(string? userToken, GraphQLBody graphQL)
        {
            using var requestMessage = new HttpRequestMessage
            {
                RequestUri = new Uri(GraphQLUrl),
                Method = HttpMethod.Post
            };
            requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", userToken);

            string content = JsonSerializer.Serialize(graphQL, _jsonSerializerOptions);
            requestMessage.Content = new StringContent(content, Encoding.UTF8, "application/json");

            var responseMessage = await _httpClientFactory.CreateClient(NamedClient.Default).SendAsync(requestMessage);
            var root = await responseMessage.Content.ReadFromJsonAsync<RootObject>(_jsonSerializerOptions);
            if (root?.Errors != null)
            {
                foreach (AnilistError error in root.Errors)
                {
                    throw new AnilistAPIException(error.ErrorMessage, error.ErrorStatus, error.Locations);
                }
            }
            return root;
        }
    }
}