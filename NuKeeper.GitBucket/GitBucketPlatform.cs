using NuKeeper.Abstractions;
using NuKeeper.Abstractions.CollaborationPlatform;
using NuKeeper.Abstractions.Configuration;
using NuKeeper.Abstractions.Formats;
using NuKeeper.Abstractions.Logging;
using Octokit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NuKeeper.Abstractions.CollaborationModels;
using Organization = NuKeeper.Abstractions.CollaborationModels.Organization;
using PullRequestRequest = NuKeeper.Abstractions.CollaborationModels.PullRequestRequest;
using Repository = NuKeeper.Abstractions.CollaborationModels.Repository;
using SearchCodeRequest = NuKeeper.Abstractions.CollaborationModels.SearchCodeRequest;
using SearchCodeResult = NuKeeper.Abstractions.CollaborationModels.SearchCodeResult;
using User = NuKeeper.Abstractions.CollaborationModels.User;
using Newtonsoft.Json;
using Octokit.Internal;

namespace NuKeeper.GitBucket
{
    public class GitBucketPlatform : ICollaborationPlatform
    {
        private readonly INuKeeperLogger _logger;
        private bool _initialised = false;

        private IGitHubClient _client;
        private Uri _apiBase;

        public GitBucketPlatform(INuKeeperLogger logger) => _logger = logger;

        public void Initialise(AuthSettings settings)
        {
            _apiBase = settings.ApiBase;

            _client = new GitHubClient(
                new Connection(
                    new Octokit.ProductHeaderValue("NuKeeper"),
                    _apiBase,
                    new InMemoryCredentialStore(new Credentials(settings.Token)),
                    new HttpClientAdapter(() => new GitBucketMessageHandler()),
                    new SimpleJsonSerializer()
                ));

            _initialised = true;
        }

        private void CheckInitialised()
        {
            if (!_initialised)
            {
                throw new NuKeeperException("GitBucket REST client has not been initialised");
            }
        }

        public async Task<User> GetCurrentUser()
        {
            CheckInitialised();

            return await ExceptionHandler(async () =>
            {
                var user = await _client.User.Current();
                _logger.Detailed($"Read gitbucket user '{user?.Login}'");
                return new User(user?.Login, user?.Name, user?.Email);
            });
        }

        public Task<IReadOnlyList<Organization>> GetOrganizations()
        {
            _logger.Error("GitBucket organizations have not yet been implemented.");
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<Repository>> GetRepositoriesForOrganisation(string projectName)
        {
            _logger.Error("GitBucket organizations have not yet been implemented.");
            throw new NotImplementedException();
        }

        public async Task<Repository> GetUserRepository(string userName, string repositoryName)
        {
            CheckInitialised();

            _logger.Detailed($"Looking for user fork for {userName}/{repositoryName}");

            return await ExceptionHandler(async () =>
            {
                try
                {
                    var result = await _client.Repository.Get(userName, repositoryName);
                    _logger.Normal($"User fork found at {result.GitUrl} for {result.Owner.Login}");
                    return new GitBucketRepository(result);
                }
                catch (NotFoundException)
                {
                    _logger.Detailed("User fork not found");
                    return null;
                }
            });
        }

        public Task<Repository> MakeUserFork(string owner, string repositoryName)
        {
            _logger.Error("GitBucket Fork API has not yet been implemented.");
            throw new NotImplementedException();
        }

        public async Task<bool> RepositoryBranchExists(string userName, string repositoryName, string branchName)
        {
            CheckInitialised();

            return await ExceptionHandler(async () =>
            {
                try
                {
                    await _client.Repository.Branch.Get(userName, repositoryName, branchName);
                    _logger.Detailed($"Branch found for {userName} / {repositoryName} / {branchName}");
                    return true;
                }
                catch (NotFoundException)
                {
                    _logger.Detailed($"No branch found for {userName} / {repositoryName} / {branchName}");
                    return false;
                }
            });
        }

        public async Task OpenPullRequest(ForkData target, PullRequestRequest request, IEnumerable<string> labels)
        {
            CheckInitialised();

            await ExceptionHandler(async () =>
            {
                _logger.Normal($"Making PR onto '{_apiBase} {target.Owner}/{target.Name} from {request.Head}");
                _logger.Detailed($"PR title: {request.Title}");

                try
                {
                    await _client.PullRequest.Create(target.Owner, target.Name, new NewPullRequest(request.Title, request.Head, request.BaseRef) { Body = request.Body });
                }
                catch (InvalidCastException)
                {
                    // Ignore InvalidCastException because of escaped response.
                    // https://github.com/gitbucket/gitbucket/issues/2306
                }

                var allPRs = await _client.PullRequest.GetAllForRepository(target.Owner, target.Name);
                var number = allPRs.Single(p => p.Title == request.Title && p.Body == request.Body).Number;

                await AddLabelsToIssue(target, number, labels);
                return Task.CompletedTask;
            });
        }

        public Task<SearchCodeResult> Search(SearchCodeRequest search)
        {
            _logger.Error($"Search has not yet been implemented for GitBucket.");
            throw new NotImplementedException();
        }

        private async Task AddLabelsToIssue(ForkData target, int issueNumber, IEnumerable<string> labels)
        {
            var labelsToApply = labels?
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

            if (labelsToApply != null && labelsToApply.Any())
            {
                _logger.Normal(
                    $"Adding label(s) '{labelsToApply.JoinWithCommas()}' to issue "
                    + $"'{_apiBase} {target.Owner}/{target.Name} {issueNumber}'");

                try
                {
                    await _client.Issue.Labels.AddToIssue(target.Owner, target.Name, issueNumber,
                        labelsToApply);
                }
                catch (ApiException ex)
                {
                    _logger.Error("Failed to add labels. Continuing", ex);
                }
            }
        }

        private async Task<T> ExceptionHandler<T>(Func<Task<T>> funcToCheck)
        {
            try
            {
                T retval = await funcToCheck();
                return retval;
            }
            catch (ApiException ex)
            {
                if (ex.HttpResponse?.Body != null)
                {
                    dynamic response = JsonConvert.DeserializeObject(ex.HttpResponse.Body.ToString());
                    if (response?.errors != null && response.errors.Count > 0)
                    {
                        throw new NuKeeperException(response.errors.First.message.ToString(), ex);
                    }
                }
                throw new NuKeeperException(ex.Message, ex);
            }
        }
    }
}
