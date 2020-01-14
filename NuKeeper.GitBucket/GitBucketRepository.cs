using NuKeeper.Abstractions.CollaborationModels;
using System;

namespace NuKeeper.GitBucket
{
    public class GitBucketRepository : Repository
    {
        public GitBucketRepository(Octokit.Repository repository)
        : base(
            repository.Name,
            repository.Archived,
            repository.Permissions != null ?
                new UserPermissions(repository.Permissions.Admin, repository.Permissions.Push, repository.Permissions.Pull) : null,
            NormaliseUri(repository.CloneUrl),
            new User(repository.Owner.Login, repository.Owner.Name, repository.Owner.Email),
            repository.Fork,
            repository.Parent != null ?
                new GitBucketRepository(repository.Parent) : null
            )
        {
        }

        private static Uri NormaliseUri(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4);
            }

            if (value.EndsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 1);
            }

            return new Uri(value, UriKind.Absolute);
        }
    }
}
