using System;
using System.Collections.Generic;
using LibGit2Sharp.Core;
using LibGit2Sharp.Core.Handles;

namespace LibGit2Sharp
{
    /// <summary>
    ///   Show changes between the working tree and the index or a tree, changes between the index and a tree, changes between two trees, or changes between two files on disk.
    ///   <para>
    ///     Copied and renamed files currently cannot be detected, as the feature is not supported by libgit2 yet.
    ///     These files will be shown as a pair of Deleted/Added files.</para>
    /// </summary>
    public class Diff
    {
        private readonly Repository repo;

        private static GitDiffOptions BuildOptions(IEnumerable<string> paths = null)
        {
            var options = new GitDiffOptions();

            if (paths == null)
            {
                return options;
            }

            options.PathSpec = GitStrArrayIn.BuildFrom(ToFilePaths(paths));
            return options;
        }

        private static FilePath[] ToFilePaths(IEnumerable<string> paths)
        {
            var filePaths = new List<FilePath>();

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    throw new ArgumentException("At least one provided path is either null or empty.", "paths");
                }

                filePaths.Add(path);
            }

            if (filePaths.Count == 0)
            {
                throw new ArgumentException("No path has been provided.", "paths");
            }

            return filePaths.ToArray();
        }

        /// <summary>
        ///   Needed for mocking purposes.
        /// </summary>
        protected Diff()
        { }

        internal Diff(Repository repo)
        {
            this.repo = repo;
        }

        /// <summary>
        ///   Show changes between two <see cref = "Tree"/>s.
        /// </summary>
        /// <param name = "oldTree">The <see cref = "Tree"/> you want to compare from.</param>
        /// <param name = "newTree">The <see cref = "Tree"/> you want to compare to.</param>
        /// <param name = "paths">The list of paths (either files or directories) that should be compared.</param>
        /// <returns>A <see cref = "TreeChanges"/> containing the changes between the <paramref name = "oldTree"/> and the <paramref name = "newTree"/>.</returns>
        public virtual TreeChanges Compare(Tree oldTree, Tree newTree, IEnumerable<string> paths = null)
        {
            //TODO: The checks below will be removed when running against next libgit2 binaries
            Ensure.ArgumentNotNull(oldTree, "oldTree");
            Ensure.ArgumentNotNull(newTree, "newTree");

            using(GitDiffOptions options = BuildOptions(paths))
            using (DiffListSafeHandle diff = BuildDiffListFromTrees(
                oldTree != null ? oldTree.Id : null,
                newTree != null ? newTree.Id : null,
                options))
            {
                return new TreeChanges(diff);
            }
        }

        private DiffListSafeHandle BuildDiffListFromTrees(ObjectId oldTree, ObjectId newTree, GitDiffOptions options)
        {
            return Proxy.git_diff_tree_to_tree(repo.Handle, options, oldTree, newTree);
        }

        /// <summary>
        ///   Show changes between two <see cref = "Blob"/>s.
        /// </summary>
        /// <param name = "oldBlob">The <see cref = "Blob"/> you want to compare from.</param>
        /// <param name = "newBlob">The <see cref = "Blob"/> you want to compare to.</param>
        /// <returns>A <see cref = "ContentChanges"/> containing the changes between the <paramref name = "oldBlob"/> and the <paramref name = "newBlob"/>.</returns>
        public virtual ContentChanges Compare(Blob oldBlob, Blob newBlob)
        {
            using (GitDiffOptions options = BuildOptions())
            {
                return new ContentChanges(repo, oldBlob, newBlob, options);
            }
        }

        private readonly IDictionary<DiffTarget, Func<Repository, TreeComparisonHandleRetriever>> handleRetrieverDispatcher = BuildHandleRetrieverDispatcher();

        private static IDictionary<DiffTarget, Func<Repository, TreeComparisonHandleRetriever>> BuildHandleRetrieverDispatcher()
        {
            return new Dictionary<DiffTarget, Func<Repository, TreeComparisonHandleRetriever>>
                       {
                           { DiffTarget.Index, r => IndexToTree(r) },
                           { DiffTarget.WorkingDirectory, r => WorkdirToTree(r) },
                           { DiffTarget.BothWorkingDirectoryAndIndex, r => WorkdirAndIndexToTree(r) },
                       };
        }

        /// <summary>
        ///   Show changes between a <see cref = "Tree"/> and a selectable target.
        /// </summary>
        /// <param name = "oldTree">The <see cref = "Tree"/> to compare from.</param>
        /// <param name = "diffTarget">The target to compare to.</param>
        /// <param name = "paths">The list of paths (either files or directories) that should be compared.</param>
        /// <returns>A <see cref = "TreeChanges"/> containing the changes between the <see cref="Tree"/> and the selected target.</returns>
        public virtual TreeChanges Compare(Tree oldTree, DiffTarget diffTarget, IEnumerable<string> paths = null)
        {
            Ensure.ArgumentNotNull(oldTree, "oldTree");

            var comparer = handleRetrieverDispatcher[diffTarget](repo);

            using (GitDiffOptions options = BuildOptions(paths))
            using (DiffListSafeHandle dl = BuildDiffListFromTreeAndComparer(oldTree.Id, comparer, options))
            {
                return new TreeChanges(dl);
            }
        }

        /// <summary>
        ///   Show changes between the working directory and the index.
        /// </summary>
        /// <param name = "paths">The list of paths (either files or directories) that should be compared.</param>
        /// <returns>A <see cref = "TreeChanges"/> containing the changes between the working directory and the index.</returns>
        public virtual TreeChanges Compare(IEnumerable<string> paths = null)
        {
            var comparer = WorkdirToIndex(repo);

            using (GitDiffOptions options = BuildOptions(paths))
            using (DiffListSafeHandle dl = BuildDiffListFromComparer(null, comparer, options))
            {
                return new TreeChanges(dl);
            }
        }

        private delegate DiffListSafeHandle TreeComparisonHandleRetriever(ObjectId id, GitDiffOptions options);

        private static TreeComparisonHandleRetriever WorkdirToIndex(Repository repo)
        {
            return (h, o) => Proxy.git_diff_workdir_to_index(repo.Handle, o);
        }

        private static TreeComparisonHandleRetriever WorkdirToTree(Repository repo)
        {
            return (h, o) => Proxy.git_diff_workdir_to_tree(repo.Handle, o, h);
        }

        private static TreeComparisonHandleRetriever WorkdirAndIndexToTree(Repository repo)
        {
            TreeComparisonHandleRetriever comparisonHandleRetriever = (h, o) =>
            {
                DiffListSafeHandle diff = null, diff2 = null;

                try
                {
                    diff = Proxy.git_diff_index_to_tree(repo.Handle, o, h);
                    diff2 = Proxy.git_diff_workdir_to_index(repo.Handle, o);
                    Proxy.git_diff_merge(diff, diff2);
                }
                catch
                {
                    diff.SafeDispose();
                    throw;
                }
                finally
                {
                    diff2.SafeDispose();
                }

                return diff;
            };

            return comparisonHandleRetriever;
        }

        private static TreeComparisonHandleRetriever IndexToTree(Repository repo)
        {
            return (h, o) => Proxy.git_diff_index_to_tree(repo.Handle, o, h);
        }

        private static DiffListSafeHandle BuildDiffListFromTreeAndComparer(ObjectId treeId, TreeComparisonHandleRetriever comparisonHandleRetriever, GitDiffOptions options)
        {
            return BuildDiffListFromComparer(treeId, comparisonHandleRetriever, options);
        }

        private static DiffListSafeHandle BuildDiffListFromComparer(ObjectId treeId, TreeComparisonHandleRetriever comparisonHandleRetriever, GitDiffOptions options)
        {
            return comparisonHandleRetriever(treeId, options);
        }
    }
}
