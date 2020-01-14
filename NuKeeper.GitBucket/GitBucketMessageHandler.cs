using System.Threading.Tasks;
using System.Net.Http;
using System.Threading;
using System.Net.Http.Headers;

namespace NuKeeper.GitBucket
{
    public class GitBucketMessageHandler : DelegatingHandler
    {
        public GitBucketMessageHandler() : base(new HttpClientHandler())
        {
        }

        protected async override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            if (request != null)
            {
                if (request.Content != null)
                {
                    var contentType = request.Content.Headers.ContentType.MediaType;
                    if (contentType == "application/x-www-form-urlencoded")
                    {
                        // GitBucket doesn't accept Content-Type: application/x-www-form-urlencoded
                        request.Content.Headers.ContentType.MediaType = "application/json";
                    }
                }

                // GitBucket deals with "token" case sensitive while GitHub doesn't.
                // octokit.net uses "Token" instead of "token".
                if (request.Headers?.Authorization?.Scheme == "Token")
                {
                    var token = request.Headers.Authorization.Parameter;
                    request.Headers.Authorization = new AuthenticationHeaderValue("token", token);
                }
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
