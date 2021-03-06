using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LibGit2Sharp.Core;
using LibGit2Sharp.Core.Handles;

namespace LibGit2Sharp
{
    /// <summary>
    ///   Provides methods to directly work against the Git object database
    ///   without involving the index nor the working directory.
    /// </summary>
    public class ObjectDatabase
    {
        private readonly Repository repo;
        private readonly ObjectDatabaseSafeHandle handle;

        /// <summary>
        ///   Needed for mocking purposes.
        /// </summary>
        protected ObjectDatabase()
        { }

        internal ObjectDatabase(Repository repo)
        {
            this.repo = repo;
            handle = Proxy.git_repository_odb(repo.Handle);

            repo.RegisterForCleanup(handle);
        }

        /// <summary>
        ///   Determines if the given object can be found in the object database.
        /// </summary>
        /// <param name="objectId">Identifier of the object being searched for.</param>
        /// <returns>True if the object has been found; false otherwise.</returns>
        public virtual bool Contains(ObjectId objectId)
        {
            Ensure.ArgumentNotNull(objectId, "objectId");

            return Proxy.git_odb_exists(handle, objectId);
        }

        /// <summary>
        ///   Inserts a <see cref="Blob"/> into the object database, created from the content of a file.
        /// </summary>
        /// <param name="path">Path to the file to create the blob from.  A relative path is allowed to
        ///   be passed if the <see cref="Repository" /> is a standard, non-bare, repository. The path
        ///   will then be considered as a path relative to the root of the working directory.</param>
        /// <returns>The created <see cref="Blob"/>.</returns>
        public virtual Blob CreateBlob(string path)
        {
            Ensure.ArgumentNotNullOrEmptyString(path, "path");

            if (repo.Info.IsBare && !Path.IsPathRooted(path))
            {
                throw new InvalidOperationException(string.Format("Cannot create a blob in a bare repository from a relative path ('{0}').", path));
            }

            ObjectId id = Path.IsPathRooted(path)
                               ? Proxy.git_blob_create_fromdisk(repo.Handle, path)
                               : Proxy.git_blob_create_fromfile(repo.Handle, path);

            return repo.Lookup<Blob>(id);
        }

        /// <summary>
        ///   Adds the provided backend to the object database with the specified priority.
        /// </summary>
        /// <param name="backend">The backend to add</param>
        /// <param name="priority">The priority at which libgit2 should consult this backend (higher values are consulted first)</param>
        public virtual void AddBackend(OdbBackend backend, int priority)
        {
            Ensure.ArgumentNotNull(backend, "backend");
            Ensure.ArgumentConformsTo<int>(priority, s => s > 0, "priority");

            Proxy.git_odb_add_backend(this.handle, backend.GitOdbBackendPointer, priority);
        }

        private class Processor
        {
            private readonly BinaryReader _reader;

            public Processor(BinaryReader reader)
            {
                _reader = reader;
            }

            public int Provider(IntPtr content, int max_length, IntPtr data)
            {
                var local = new byte[max_length];
                int numberOfReadBytes = _reader.Read(local, 0, max_length);

                Marshal.Copy(local, 0, content, numberOfReadBytes);

                return numberOfReadBytes;
            }
        }

        /// <summary>
        ///   Inserts a <see cref="Blob"/> into the object database, created from the content of a data provider.
        /// </summary>
        /// <param name="reader">The reader that will provide the content of the blob to be created.</param>
        /// <param name="hintpath">The hintpath is used to determine what git filters should be applied to the object before it can be placed to the object database.</param>
        /// <returns>The created <see cref="Blob"/>.</returns>
        public virtual Blob CreateBlob(BinaryReader reader, string hintpath = null)
        {
            Ensure.ArgumentNotNull(reader, "reader");

            var proc = new Processor(reader);
            ObjectId id = Proxy.git_blob_create_fromchunks(repo.Handle, hintpath, proc.Provider);

            return repo.Lookup<Blob>(id);
        }

        /// <summary>
        ///   Inserts a <see cref = "Tree"/> into the object database, created from a <see cref = "TreeDefinition"/>.
        /// </summary>
        /// <param name = "treeDefinition">The <see cref = "TreeDefinition"/>.</param>
        /// <returns>The created <see cref = "Tree"/>.</returns>
        public virtual Tree CreateTree(TreeDefinition treeDefinition)
        {
            Ensure.ArgumentNotNull(treeDefinition, "treeDefinition");

            return treeDefinition.Build(repo);
        }

        /// <summary>
        ///   Inserts a <see cref = "Commit"/> into the object database, referencing an existing <see cref = "Tree"/>.
        /// </summary>
        /// <param name = "message">The description of why a change was made to the repository.</param>
        /// <param name = "author">The <see cref = "Signature" /> of who made the change.</param>
        /// <param name = "committer">The <see cref = "Signature" /> of who added the change to the repository.</param>
        /// <param name = "tree">The <see cref = "Tree"/> of the <see cref = "Commit"/> to be created.</param>
        /// <param name = "parents">The parents of the <see cref = "Commit"/> to be created.</param>
        /// <returns>The created <see cref = "Commit"/>.</returns>
        public virtual Commit CreateCommit(string message, Signature author, Signature committer, Tree tree, IEnumerable<Commit> parents)
        {
            return CreateCommit(message, author, committer, tree, parents, null);
        }

        internal Commit CreateCommit(string message, Signature author, Signature committer, Tree tree, IEnumerable<Commit> parents, string referenceName)
        {
            Ensure.ArgumentNotNull(message, "message");
            Ensure.ArgumentNotNull(author, "author");
            Ensure.ArgumentNotNull(committer, "committer");
            Ensure.ArgumentNotNull(tree, "tree");
            Ensure.ArgumentNotNull(parents, "parents");

            string prettifiedMessage = Proxy.git_message_prettify(message);
            IEnumerable<ObjectId> parentIds = parents.Select(p => p.Id);

            ObjectId commitId = Proxy.git_commit_create(repo.Handle, referenceName, author, committer, prettifiedMessage, tree, parentIds);

            return repo.Lookup<Commit>(commitId);
        }
    }
}
