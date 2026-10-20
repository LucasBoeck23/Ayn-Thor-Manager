using System.Runtime.InteropServices;
using System.Collections.Concurrent;
using AynThorManager.Core.Interfaces;
using AynThorManager.Core.Models;
using AynThorManager.Infrastructure.Files;

namespace AynThorManager.Infrastructure.Mount;

public sealed class ThorFuseFilesystem
{
    private readonly IAdbFileGateway _gateway;
    private readonly FileCache _cache;

    private readonly string _remoteRoot;

    private static readonly TimeSpan StatCacheTtl = TimeSpan.FromSeconds(5);
    private readonly ConcurrentDictionary<string, (ThorFileEntry Entry, DateTime At)> _statCache =
        new(StringComparer.Ordinal);

    private static readonly uint CurrentUid = getuid();
    private static readonly uint CurrentGid = getgid();

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getuid")]
    private static extern uint getuid();

    [System.Runtime.InteropServices.DllImport("libc", EntryPoint = "getgid")]
    private static extern uint getgid();

    public ThorFuseFilesystem(IAdbFileGateway gateway, FileCache cache, string remoteRoot = "/storage/emulated/0")
    {
        ArgumentNullException.ThrowIfNull(gateway);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteRoot);

        _gateway = gateway;
        _cache = cache;
        _remoteRoot = "/" + remoteRoot.Trim('/');
    }

    private string ToRemote(string kernelPath)
    {
        if (string.IsNullOrEmpty(kernelPath) || kernelPath == "/")
            return _remoteRoot;

        return _remoteRoot + "/" + kernelPath.TrimStart('/');
    }

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
            chmod = Chmod,
            chown = Chown,
            utimens = Utimens,
        };
    }

    private int Getattr(string path, ref FuseStat stat, IntPtr fi)
    {
        var local = default(FuseStat);
        var result = LibFuseInterop.Guard(() =>
        {
            if (IsRoot(path))
            {
                local.st_mode = FuseMode.S_IFDIR | 0b111_101_101;
                local.st_nlink = 2;
                local.st_size = 0;
                local.st_uid = CurrentUid;
                local.st_gid = CurrentGid;
                return 0;
            }

            if (_cache.IsCached(ToRemote(path)))
            {
                var localFile = _cache.LocalPathFor(ToRemote(path));
                if (File.Exists(localFile))
                {
                    var info = new FileInfo(localFile);
                    local.st_mode = FuseMode.S_IFREG | 0b110_100_100u;
                    local.st_nlink = 1;
                    local.st_size = info.Length;
                    local.st_uid = CurrentUid;
                    local.st_gid = CurrentGid;
                    local.st_mtime = new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds();
                    return 0;
                }
            }

            var remote = ToRemote(path);
            if (TryGetCachedStat(remote, out var cached))
            {
                FillStat(ref local, cached);
                return 0;
            }

            var entry = Unwrap(_gateway.StatAsync(remote).GetAwaiter().GetResult());
            _statCache[remote] = (entry, DateTime.UtcNow);
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
            filler(buf, ".", IntPtr.Zero, 0, 0);
            filler(buf, "..", IntPtr.Zero, 0, 0);

            var dir = ToRemote(path);
            var entries = Unwrap(_gateway.ListAsync(dir).GetAwaiter().GetResult());
            var now = DateTime.UtcNow;
            foreach (var entry in entries)
            {
                if (entry.Name is "." or ".." || string.IsNullOrEmpty(entry.Name))
                    continue;

                _statCache[JoinRemote(dir, entry.Name)] = (entry, now);

                if (filler(buf, entry.Name, IntPtr.Zero, 0, 0) != 0)
                    break;
            }

            return 0;
        });
    }

    private int Open(string path, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
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

    private int Write(string path, IntPtr buf, nuint size, long offset, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
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

            _cache.MarkDirty(remote);
            InvalidateStat(remote);
            return count;
        });
    }

    private int Create(string path, uint mode, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            var remote = ToRemote(path);
            Unwrap(_cache.CreateLocal(remote));
            InvalidateStat(remote);
            return 0;
        });
    }

    private int Truncate(string path, long size, IntPtr fi)
    {
        return LibFuseInterop.Guard(() =>
        {
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

            var truncated = ToRemote(path);
            _cache.MarkDirty(truncated);
            InvalidateStat(truncated);
            return 0;
        });
    }

    private int Flush(string path, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            Unwrap(_cache.FlushAsync(ToRemote(path)).GetAwaiter().GetResult());
            return 0;
        });
    }

    private int Release(string path, ref FuseFileInfo fi)
    {
        return LibFuseInterop.Guard(() =>
        {
            Unwrap(_cache.FlushAsync(ToRemote(path)).GetAwaiter().GetResult());
            return 0;
        });
    }

    private int Unlink(string path)
    {
        return LibFuseInterop.Guard(() =>
        {
            var remote = ToRemote(path);
            if (_cache.IsCached(remote) && _cache.IsDirty(remote) && !RemoteExists(remote))
            {
                _cache.Evict(remote);
                InvalidateStat(remote);
                return 0;
            }

            Unwrap(_gateway.DeleteAsync(remote).GetAwaiter().GetResult());
            _cache.Evict(remote);
            InvalidateStat(remote);
            return 0;
        });
    }

    private int Mkdir(string path, uint mode)
    {
        return LibFuseInterop.Guard(() =>
        {
            var remote = ToRemote(path);
            Unwrap(_gateway.MakeDirAsync(remote).GetAwaiter().GetResult());
            InvalidateStat(remote);
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
            InvalidateStat(remote);
            return 0;
        });
    }

    private int Rename(string from, string to, uint flags)
    {
        return LibFuseInterop.Guard(() =>
        {
            var remoteFrom = ToRemote(from);
            var remoteTo = ToRemote(to);
            Unwrap(_cache.FlushAsync(remoteFrom).GetAwaiter().GetResult());
            Unwrap(_gateway.RenameAsync(remoteFrom, remoteTo).GetAwaiter().GetResult());
            _cache.Evict(remoteFrom);
            InvalidateStat(remoteFrom);
            InvalidateStat(remoteTo);
            return 0;
        });
    }

    private int Chmod(string path, uint mode, IntPtr fi) => 0;

    private int Chown(string path, uint uid, uint gid, IntPtr fi) => 0;

    private int Utimens(string path, IntPtr tv, IntPtr fi) => 0;

    private bool RemoteExists(string remotePath) =>
        _gateway.StatAsync(remotePath).GetAwaiter().GetResult().IsSuccess;

    private bool TryGetCachedStat(string remotePath, out ThorFileEntry entry)
    {
        if (_statCache.TryGetValue(remotePath, out var hit) && DateTime.UtcNow - hit.At < StatCacheTtl)
        {
            entry = hit.Entry;
            return true;
        }

        entry = default!;
        return false;
    }

    private static string JoinRemote(string dir, string name) =>
        dir.TrimEnd('/') + "/" + name;

    private void InvalidateStat(string remotePath) => _statCache.TryRemove(remotePath, out _);

    private static bool IsRoot(string path) =>
        string.IsNullOrEmpty(path) || path == "/";

    private static void FillStat(ref FuseStat stat, ThorFileEntry entry)
    {
        var typeBits = entry.Type switch
        {
            ThorFileType.Directory => FuseMode.S_IFDIR,
            ThorFileType.Symlink => FuseMode.S_IFLNK,
            _ => FuseMode.S_IFREG,
        };

        var ownerBits = entry.Type == ThorFileType.Directory ? 0b111_000_000u : 0b110_000_000u;
        stat.st_mode = typeBits | PermissionBits(entry.Permissions, entry.Type) | ownerBits;
        stat.st_size = entry.Size;
        stat.st_nlink = entry.Type == ThorFileType.Directory ? 2u : 1u;
        stat.st_uid = CurrentUid;
        stat.st_gid = CurrentGid;

        if (entry.ModifiedTime is { } mtime)
            stat.st_mtime = mtime.ToUnixTimeSeconds();
    }

    private static uint PermissionBits(string permissions, ThorFileType type)
    {
        var fallback = type == ThorFileType.Directory ? 0b111_101_101u : 0b110_100_100u;

        if (string.IsNullOrEmpty(permissions) || permissions.Length < 9)
            return fallback;

        uint bits = 0;
        for (var i = 0; i < 9; i++)
        {
            bits <<= 1;
            if (permissions[i] != '-')
                bits |= 1;
        }

        return bits;
    }

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

    private static T Unwrap<T>(Result<T> result)
    {
        if (result.IsSuccess)
            return result.Value!;

        throw new FuseException(ErrnoFor(result.Error), result.Error?.Message);
    }

    private static void Unwrap(Result result)
    {
        if (result.IsSuccess)
            return;

        throw new FuseException(ErrnoFor(result.Error), result.Error?.Message);
    }

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
