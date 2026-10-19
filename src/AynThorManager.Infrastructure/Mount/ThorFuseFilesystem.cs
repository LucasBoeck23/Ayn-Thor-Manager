using System.Runtime.InteropServices;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Files;

namespace AynThorManager.Infrastructure.Mount;

/// <summary>
/// The FUSE→ADB translator: it supplies the callback bodies for the
/// <see cref="FuseOperations"/> struct (the plumbing from task 3.1) and turns each kernel
/// filesystem request into high-level operations on <see cref="IAdbFileGateway"/> and
/// <see cref="FileCache"/>. This is the heart of the "no third-party binary" decision —
/// the thing adbfs used to do is now auditable code in the repo.
///
/// How the operations map (design "Leitura/escrita de arquivos" + Properties 3/4/6):
/// <list type="bullet">
///   <item><description>
///     <c>getattr</c>/<c>readdir</c> are <b>lazy</b>: they only ever read metadata
///     (<c>ls</c>/<c>stat</c>) and never move file content (Property 3).
///   </description></item>
///   <item><description>
///     <c>open</c>/<c>read</c> pull the file into the cache once, then serve reads from the
///     fast local copy.
///   </description></item>
///   <item><description>
///     <c>write</c>/<c>create</c>/<c>truncate</c> write into the cache and mark it dirty;
///     <c>flush</c>/<c>release</c> push a dirty file back to the Thor and nothing else
///     (Property 4).
///   </description></item>
///   <item><description>
///     <c>unlink</c>/<c>mkdir</c>/<c>rmdir</c>/<c>rename</c> map straight to the gateway.
///   </description></item>
/// </list>
///
/// Every callback body runs inside <see cref="LibFuseInterop.Guard"/>, so no managed
/// exception can ever unwind into libfuse — a gateway failure becomes a clean
/// <c>-errno</c> (a missing path is <c>-ENOENT</c>, a broken ADB transfer is <c>-EIO</c>)
/// and the mount stays alive (Property 6). The gateway/cache are async while FUSE callbacks
/// are synchronous, so each body bridges by blocking on the task
/// (<c>.GetAwaiter().GetResult()</c>) — acceptable because libfuse already calls us on its
/// own worker threads, one per in-flight request.
/// </summary>
public sealed class ThorFuseFilesystem
{
    private readonly IAdbFileGateway _gateway;
    private readonly FileCache _cache;

    // The kernel addresses everything under the mount as absolute paths rooted at "/"
    // (e.g. "/foo.txt"). The Thor's user storage lives under a base path (default
    // "/sdcard"), so every kernel path is rebased onto it before touching the gateway/cache
    // (see ToRemote). Without this, "/foo.txt" would hit the Android root filesystem, which
    // is read-only and does not contain the user's files — the exact PATH_NOT_FOUND /
    // PERMISSION_DENIED failures seen on create/mkdir/write.
    private readonly string _remoteRoot;

    // The mount is owned by the user who started the app. Reporting that user's uid/gid on
    // every node is essential: the FUSE kernel does its own permission check on the parent
    // directory for namespace ops (rename/unlink/mkdir) using the uid/gid getattr reports.
    // If nodes came back owned by root (uid 0), the unprivileged user would get EACCES on
    // rename/delete BEFORE our callback ever ran (the kernel rejects it). So we stamp the
    // caller's own uid/gid, matching how the mount itself is tagged (user_id/group_id).
    private static readonly uint CurrentUid = getuid();
    private static readonly uint CurrentGid = getgid();

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getuid")]
    private static extern uint getuid();

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getgid")]
    private static extern uint getgid();

    /// <summary>
    /// Builds the translator over the gateway (talks to the active Thor) and the per-mount
    /// cache (the local <c>/tmp</c> backing store).
    /// </summary>
    public ThorFuseFilesystem(IAdbFileGateway gateway, FileCache cache, string remoteRoot = "/storage/emulated/0")
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteRoot);

        _gateway = gateway;
        _cache = cache;
        _remoteRoot = "/" + remoteRoot.Trim('/');
    }

    /// <summary>
    /// Rebases a kernel path (always rooted at the mount, e.g. <c>/foo/bar</c>) onto the
    /// Thor's base path (<see cref="_remoteRoot"/>, e.g. <c>/sdcard</c>) so the gateway and
    /// cache operate on the real device path (<c>/sdcard/foo/bar</c>). The mount root maps
    /// to the base path itself.
    /// </summary>
    private string ToRemote(string kernelPath)
    {
        if (string.IsNullOrEmpty(kernelPath) || kernelPath == "/")
            return _remoteRoot;

        return _remoteRoot + "/" + kernelPath.TrimStart('/');
    }

    /// <summary>
    /// Builds the <see cref="FuseOperations"/> table wired to this translator's callbacks.
    /// Hand the result to <see cref="FuseOperationsHandle"/> and <c>fuse_main_real</c>; the
    /// unimplemented slots stay null so libfuse answers them with its own defaults.
    /// </summary>
    internal FuseOperations CreateOperations()
    {
        return new FuseOperations
        {
            getattr = Getattr,
            readdir = Readdir,
            open = Open,
            read = Read,
            write = Write,
            create = Create,
            truncate = Truncate,
            flush = Flush,
            release = Release,
            unlink = Unlink,
            mkdir = Mkdir,
            rmdir = Rmdir,
            rename = Rename,
        };
    }

    // ----- Metadata (lazy) -------------------------------------------------------------

    private int Getattr(string path, ref FuseStat stat, IntPtr fi)
    {
        // Can't capture a ref parameter in the Guard lambda; copy out, fill locally, copy
        // back only on success.
        var local = default(FuseStat);
        var result = LibFuseInterop.Guard(() =>
        {
            // The mount root is always a directory, even before the Thor answers anything.
            if (IsRoot(path))
            {
                local.st_mode = FuseMode.S_IFDIR | 0b111_101_101; // 0755
                local.st_nlink = 2;
                local.st_size = 0;
                local.st_uid = CurrentUid;
                local.st_gid = CurrentGid;
                return 0;
            }

            // A file that only exists in the local cache (just created/written but not yet
            // pushed) must still answer getattr — otherwise the kernel's post-create stat
            // hits the Thor via `ls`, finds nothing, and the write is lost with ENOENT.
            // Report it from the local backing file so create → write → flush works before
            // the first push.
            if (_cache.IsCached(ToRemote(path)))
            {
                var localFile = _cache.LocalPathFor(ToRemote(path));
                if (File.Exists(localFile))
                {
                    var info = new FileInfo(localFile);
                    local.st_mode = FuseMode.S_IFREG | 0b110_100_100u; // 0644
                    local.st_nlink = 1;
                    local.st_size = info.Length;
                    local.st_uid = CurrentUid;
                    local.st_gid = CurrentGid;
                    local.st_mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
                    return 0;
                }
            }

            var entry = Unwrap(_gateway.StatAsync(ToRemote(path)).GetAwaiter().GetResult());
            FillStat(ref local, entry);
            return 0;
        });

        if (result == 0)
            stat = local;

        return result;
    }

    private int Readdir(string path, IntPtr buf, FuseFillDir filler, long offset, IntPtr fi, int flags)
    {
        return LibFuseInterop.Guard(() =>
        {
            // "." and ".." are always present; the kernel expects them in every listing.
            filler(buf, ".", IntPtr.Zero, 0, 0);
            filler(buf, "..", IntPtr.Zero, 0, 0);

            var entries = Unwrap(_gateway.ListAsync(ToRemote(path)).GetAwaiter().GetResult());
            foreach (var entry in entries)
            {
                // Skip the self/parent entries that `ls -la` itself emits so they are not
                // added twice.
                if (entry.Name is "." or ".." || string.IsNullOrEmpty(entry.Name))
                    continue;

                // A non-zero return means libfuse's buffer is full; stop filling.
                if (filler(buf, entry.Name, IntPtr.Zero, 0, 0) != 0)
                    break;
            }

            return 0;
        });
    }

    // ----- Read path -------------------------------------------------------------------

    private int Open(string path, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            // Pull the remote file into the cache once; later reads serve from the local
            // copy. No handle is stashed in fi — the cache is keyed by path.
            Unwrap(_cache.EnsureCachedAsync(ToRemote(path)).GetAwaiter().GetResult());
            return 0;
        });
    }

    private int Read(string path, IntPtr buf, nuint size, long offset, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            var localPath = Unwrap(_cache.EnsureCachedAsync(ToRemote(path)).GetAwaiter().GetResult());

            using var stream = new FileStream(
                localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            // A read past EOF returns 0 bytes, not an error.
            if (offset >= stream.Length)
                return 0;

            var toRead = (int)Math.Min((long)size, stream.Length - offset);
            if (toRead <= 0)
                return 0;

            stream.Seek(offset, SeekOrigin.Begin);

            var managed = new byte[toRead];
            var read = ReadFully(stream, managed, toRead);

            Marshal.Copy(managed, 0, buf, read);
            return read;
        });
    }

    // ----- Write path ------------------------------------------------------------------

    private int Write(string path, IntPtr buf, nuint size, long offset, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            // The cache file must exist before we can write at an offset. If the file was
            // never opened/created (unusual, but possible), back it with an empty local
            // file so the write has somewhere to land.
            var remote = ToRemote(path);
            var localPath = _cache.IsCached(remote)
                ? _cache.LocalPathFor(remote)
                : Unwrap(_cache.CreateLocal(remote));

            var count = (int)size;
            var managed = new byte[count];
            Marshal.Copy(buf, managed, 0, count);

            using (var stream = new FileStream(
                localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
            {
                stream.Seek(offset, SeekOrigin.Begin);
                stream.Write(managed, 0, count);
            }

            // Local copy diverged from the Thor: it must be pushed on flush (Property 4).
            _cache.MarkDirty(remote);
            return count;
        });
    }

    private int Create(string path, uint mode, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            // A brand-new file has no remote content: create an empty local backing file
            // and mark it dirty so the eventual flush pushes it to the Thor.
            Unwrap(_cache.CreateLocal(ToRemote(path)));
            return 0;
        });
    }

    private int Truncate(string path, long size, IntPtr fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            // Truncating to zero is the common "overwrite from scratch" case (e.g. a
            // redirect `> file`): drop any remote content and start from an empty local
            // file. Truncating to a non-zero size shortens/extends the current cached copy.
            string localPath;
            if (size == 0)
            {
                localPath = Unwrap(_cache.CreateLocal(ToRemote(path)));
            }
            else
            {
                var remote = ToRemote(path);
                localPath = _cache.IsCached(remote)
                    ? _cache.LocalPathFor(remote)
                    : Unwrap(_cache.EnsureCachedAsync(remote).GetAwaiter().GetResult());

                using var stream = new FileStream(
                    localPath, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite);
                stream.SetLength(size);
            }

            _cache.MarkDirty(ToRemote(path));
            return 0;
        });
    }

    // ----- Flush / release (push if dirty) ---------------------------------------------

    private int Flush(string path, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            // Push only when the local copy is dirty; a clean file is a no-op (Property 4).
            Unwrap(_cache.FlushAsync(ToRemote(path)).GetAwaiter().GetResult());
            return 0;
        });
    }

    private int Release(string path, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            // The kernel ignores release's return value, but still push any pending write
            // here so a close that skipped flush does not lose data (Property 4).
            Unwrap(_cache.FlushAsync(ToRemote(path)).GetAwaiter().GetResult());
            return 0;
        });
    }

    // ----- Namespace operations --------------------------------------------------------

    private int Unlink(string path)
    {
        return LibFuseInterop.Guard(() =>
        {
            var remote = ToRemote(path);
            // If the file only ever lived in the local cache (created/written, never
            // pushed), there is nothing on the Thor to delete — just drop the cache entry.
            if (_cache.IsCached(remote) && _cache.IsDirty(remote) && !RemoteExists(remote))
            {
                _cache.Evict(remote);
                return 0;
            }

            Unwrap(_gateway.DeleteAsync(remote).GetAwaiter().GetResult());
            _cache.Evict(remote);
            return 0;
        });
    }

    private int Mkdir(string path, uint mode)
    {
        return LibFuseInterop.Guard(() =>
        {
            Unwrap(_gateway.MakeDirAsync(ToRemote(path)).GetAwaiter().GetResult());
            return 0;
        });
    }

    private int Rmdir(string path)
    {
        return LibFuseInterop.Guard(() =>
        {
            var remote = ToRemote(path);
            Unwrap(_gateway.DeleteAsync(remote).GetAwaiter().GetResult());
            _cache.Evict(remote);
            return 0;
        });
    }

    private int Rename(string from, string to, uint flags)
    {
        return LibFuseInterop.Guard(() =>
        {
            var remoteFrom = ToRemote(from);
            // A source that was created/written but not yet pushed exists only in the local
            // cache; the device-side `mv` would not find it. Flush it first so the rename
            // operates on a file that actually exists on the Thor.
            Unwrap(_cache.FlushAsync(remoteFrom).GetAwaiter().GetResult());
            Unwrap(_gateway.RenameAsync(remoteFrom, ToRemote(to)).GetAwaiter().GetResult());
            // The old cache entry no longer maps to a live remote path.
            _cache.Evict(remoteFrom);
            return 0;
        });
    }

    // ----- Helpers ---------------------------------------------------------------------

    /// <summary>Whether the Thor has a node at <paramref name="remotePath"/> (a cheap stat).</summary>
    private bool RemoteExists(string remotePath) =>
        _gateway.StatAsync(remotePath).GetAwaiter().GetResult().IsSuccess;

    private static bool IsRoot(string path) =>
        string.IsNullOrEmpty(path) || path == "/";

    /// <summary>
    /// Fills a <see cref="FuseStat"/> from a parsed <see cref="ThorFileEntry"/>: the type
    /// bits (<c>S_IFDIR</c>/<c>S_IFREG</c>/<c>S_IFLNK</c>) OR'd with the parsed permission
    /// bits, plus size and modified time.
    /// </summary>
    private static void FillStat(ref FuseStat stat, ThorFileEntry entry)
    {
        var typeBits = entry.Type switch
        {
            ThorFileType.Directory => FuseMode.S_IFDIR,
            ThorFileType.Symlink => FuseMode.S_IFLNK,
            _ => FuseMode.S_IFREG,
        };

        // Force the owner's write/execute bits on: we report the node as owned by the
        // mounting user, but the Thor's permission string belongs to Android's own uid
        // namespace (often root/sdcard_rw). Without the owner having rwx here, tools and
        // the kernel would deny writes/rename/mkdir on nodes the user legitimately controls
        // through the mount. Real authorization still happens on the Thor: a denied ADB op
        // surfaces as an errno from the gateway.
        var ownerBits = entry.Type == ThorFileType.Directory ? 0b111_000_000u : 0b110_000_000u;
        stat.st_mode = typeBits | PermissionBits(entry.Permissions, entry.Type) | ownerBits;
        stat.st_size = entry.Size;
        stat.st_nlink = entry.Type == ThorFileType.Directory ? 2u : 1u;
        stat.st_uid = CurrentUid;
        stat.st_gid = CurrentGid;

        if (entry.ModifiedTime is { } mtime)
            stat.st_mtime = mtime.ToUnixTimeSeconds();
    }

    /// <summary>
    /// Translates a <c>ls -la</c> permission string (e.g. <c>rwxr-xr-x</c>, no leading type
    /// char) into the 9 POSIX permission bits. Falls back to a sane default (<c>0755</c>
    /// for directories, <c>0644</c> for files) when the string is missing or malformed, so
    /// a parse gap never produces an unreadable node.
    /// </summary>
    private static uint PermissionBits(string permissions, ThorFileType type)
    {
        var fallback = type == ThorFileType.Directory ? 0b111_101_101u : 0b110_100_100u;

        if (string.IsNullOrEmpty(permissions) || permissions.Length < 9)
            return fallback;

        uint bits = 0;
        // Each triad is r,w,x; a set bit is any character other than '-'.
        for (var i = 0; i < 9; i++)
        {
            bits <<= 1;
            if (permissions[i] != '-')
                bits |= 1;
        }

        return bits;
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes (or fewer only at EOF) from
    /// <paramref name="stream"/> into <paramref name="buffer"/>, looping because a single
    /// <c>Read</c> may return a short count. Returns the number of bytes actually read.
    /// </summary>
    private static int ReadFully(Stream stream, byte[] buffer, int count)
    {
        var total = 0;
        while (total < count)
        {
            var n = stream.Read(buffer, total, count - total);
            if (n == 0)
                break;
            total += n;
        }

        return total;
    }

    /// <summary>
    /// Unwraps a <see cref="Result{T}"/>, returning its value or throwing a
    /// <see cref="FuseException"/> carrying the errno that matches the error — so
    /// <see cref="LibFuseInterop.Guard"/> turns the failure into the right <c>-errno</c>
    /// (a missing path becomes <c>-ENOENT</c>, an ADB failure <c>-EIO</c>, Property 6).
    /// </summary>
    private static T Unwrap<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return result.Value!;

        throw new FuseException(ErrnoFor(result.Error), result.Error?.Message);
    }


    /// <summary>
    /// Unwraps a non-generic <see cref="Result"/>, throwing a <see cref="FuseException"/>
    /// with the matching errno when it failed.
    /// </summary>
    private static void Unwrap(Result result)
    {
        if (result.IsSuccess)
            return;

        throw new FuseException(ErrnoFor(result.Error), result.Error?.Message);
    }

    /// <summary>
    /// Maps a gateway/cache <see cref="Error"/> code to the POSIX errno the kernel should
    /// see: a not-found path is <c>ENOENT</c>, a permission/read-only error is
    /// <c>EACCES</c>, a name clash is <c>EEXIST</c>, an invalid path is <c>EINVAL</c>, and
    /// anything else (a broken ADB transfer, a cache write failure) falls back to the
    /// generic <c>EIO</c> (design: ADB failure → <c>-EIO</c>).
    /// </summary>
    private static int ErrnoFor(Error? error)
    {
        return error?.Code switch
        {
            "PATH_NOT_FOUND" => FuseErrno.ENOENT,
            "PERMISSION_DENIED" => FuseErrno.EACCES,
            "NAME_ALREADY_EXISTS" => FuseErrno.EEXIST,
            "INVALID_PATH" => FuseErrno.EINVAL,
            _ => FuseErrno.EIO,
        };
    }
}
