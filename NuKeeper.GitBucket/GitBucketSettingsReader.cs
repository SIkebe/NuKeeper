using NuKeeper.Abstractions;
using NuKeeper.Abstractions.CollaborationPlatform;
using NuKeeper.Abstractions.Configuration;
using System;
using System.Linq;
using NuKeeper.Abstractions.Git;
using System.Threading.Tasks;
using System.Net.Http;
using System.Net.Http.Headers;

namespace NuKeeper.GitBucket
{
    public class GitBucketSettingsReader : ISettingsReader
    {
        private readonly IEnvironmentVariablesProvider _environmentVariablesProvider;
        private const string PlatformHost = "gitbucket";
        private const string UrlPattern = "http(s)://yourgitbucket/git/{owner}/{reponame}.git";
        private readonly IGitDiscoveryDriver _gitDriver;

        public GitBucketSettingsReader(IGitDiscoveryDriver gitDriver, IEnvironmentVariablesProvider environmentVariablesProvider)
        {
            _environmentVariablesProvider = environmentVariablesProvider;
            _gitDriver = gitDriver;
        }

        public Platform Platform => Platform.GitBucket;

        public async Task<bool> CanRead(Uri repositoryUri)
        {
            if (repositoryUri == null)
            {
                return false;
            }

            try
            {
                Uri baseAddress;
                string requestUri;
                if (repositoryUri.AbsoluteUri.Contains("api/v3"))
                {
                    baseAddress = repositoryUri;
                    requestUri = $"gitbucket/plugins";
                }
                else
                {
                    baseAddress = GetBaseAddress(repositoryUri);
                    requestUri = $"api/v3/gitbucket/plugins";
                }

                var client = new HttpClient { BaseAddress = baseAddress };
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                // There is no real identifier for GitBucket repos so try to get the plugin endpoint
                var response = await client.GetAsync(requestUri);
                if (response.IsSuccessStatusCode)
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("No valid GitBucket repo during repo check\n\r{0}", ex.Message);
            }

            return false;
        }

        public void UpdateCollaborationPlatformSettings(CollaborationPlatformSettings settings)
        {
            var envToken = _environmentVariablesProvider.GetEnvironmentVariable("NuKeeper_gitbucket_token");
            settings.Token = Concat.FirstValue(envToken, settings.Token);
            settings.ForkMode ??= ForkMode.SingleRepositoryOnly;
        }

        public async Task<RepositorySettings> RepositorySettings(Uri repositoryUri, string targetBranch = null)
        {
            if (repositoryUri == null)
            {
                throw new NuKeeperException($"The provided uri was not in the correct format. Provided null and format should be {UrlPattern}");
            }

            var settings = repositoryUri.IsFile
                ? await CreateSettingsFromLocal(repositoryUri, targetBranch)
                : CreateSettingsFromRemote(repositoryUri, targetBranch);

            if (settings == null)
            {
                throw new NuKeeperException($"The provided uri was not in the correct format. Provided {repositoryUri} and format should be {UrlPattern}");
            }

            return settings;
        }

        private async Task<RepositorySettings> CreateSettingsFromLocal(Uri repositoryUri, string targetBranch)
        {
            var remoteInfo = new RemoteInfo();

            var localFolder = repositoryUri;
            if (await _gitDriver.IsGitRepo(repositoryUri))
            {
                // Check the origin remotes
                var origin = await _gitDriver.GetRemoteForPlatform(repositoryUri, PlatformHost);

                if (origin != null)
                {
                    remoteInfo.LocalRepositoryUri = await _gitDriver.DiscoverRepo(repositoryUri); // Set to the folder, because we found a remote git repository
                    repositoryUri = origin.Url;
                    remoteInfo.BranchName = targetBranch ?? await _gitDriver.GetCurrentHead(remoteInfo.LocalRepositoryUri);
                    remoteInfo.RemoteName = origin.Name;
                    remoteInfo.WorkingFolder = localFolder;
                }
            }
            else
            {
                throw new NuKeeperException("No git repository found");
            }

            // general pattern is http(s)://yourgitbucket/git/owner/reponame.git
            // from this we extract owner and repo name
            var path = repositoryUri.AbsolutePath;
            var pathParts = path.Split('/')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (pathParts.Count != 3)
            {
                throw new NuKeeperException($"The provided uri was not in the correct format. Provided {repositoryUri} and format should be {UrlPattern}");
            }

            var repoOwner = pathParts[1];
            var repoName = pathParts[2].Replace(".git", string.Empty);
            var uriBuilder = new UriBuilder(repositoryUri) { Path = "/api/v3/" };

            return new RepositorySettings
            {
                ApiUri = uriBuilder.Uri,
                RepositoryUri = repositoryUri,
                RepositoryName = repoName,
                RepositoryOwner = repoOwner,
                RemoteInfo = remoteInfo
            };
        }

        private RepositorySettings CreateSettingsFromRemote(Uri repositoryUri, string targetBranch)
        {
            if (repositoryUri == null)
            {
                throw new NuKeeperException($"The provided uri was not in the correct format. Provided null and format should be {UrlPattern}");
            }

            // general pattern is http(s)://yourgitbucket/git/owner/reponame.git
            // from this we extract owner and repo name
            var path = repositoryUri.AbsolutePath;
            var pathParts = path.Split('/')
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();

            if (pathParts.Count != 3)
            {
                throw new NuKeeperException($"The provided uri was not in the correct format. Provided {repositoryUri} and format should be {UrlPattern}");
            }

            var repoOwner = pathParts[1];
            var repoName = pathParts[2].Replace(".git", string.Empty);
            var uriBuilder = new UriBuilder(repositoryUri) { Path = "/api/v3/" };

            return new RepositorySettings
            {
                ApiUri = uriBuilder.Uri,
                RepositoryUri = repositoryUri,
                RepositoryName = repoName,
                RepositoryOwner = repoOwner,
                RemoteInfo = targetBranch != null ? new RemoteInfo { BranchName = targetBranch } : null
            };
        }

        private Uri GetBaseAddress(Uri repoUri)
        {
            var newSegments = repoUri.Segments.Take(repoUri.Segments.Length - 3).ToArray();
            var uriBuilder = new UriBuilder(repoUri)
            {
                Path = string.Concat(newSegments)
            };

            return uriBuilder.Uri;
        }
    }
}
