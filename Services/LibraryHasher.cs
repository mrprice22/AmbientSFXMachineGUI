using System;
using System.Collections.Specialized;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Windows.Threading;
using AmbientSFXMachineGUI.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AmbientSFXMachineGUI.Services;

/// <summary>
/// Background SHA-256 hasher: subscribes to <see cref="AudioLibrary.Entries"/>,
/// streams each unhashed file on a worker thread, and assigns the resulting
/// hash back to the library. Results are cached in library.json; cached hashes
/// are reused when the file's size + mtime are unchanged.
/// Exposes observable progress for binding to a header indicator.
/// </summary>
public sealed partial class LibraryHasher : ObservableObject, IDisposable
{
    private readonly AudioLibrary _library;
    private readonly LibraryCacheStore _cache;
    private readonly Channel<AudioFileEntry> _queue =
        Channel.CreateUnbounded<AudioFileEntry>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();
    private readonly Dispatcher _dispatcher;
    private readonly Task _worker;

    private int _enqueued;
    private int _completed;

    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _processedCount;
    [ObservableProperty] private int _pendingCount;
    [ObservableProperty] private bool _isScanning;

    public double Progress => TotalCount == 0 ? 0 : (double)ProcessedCount / TotalCount;

    public LibraryHasher(AudioLibrary library, LibraryCacheStore cache, Dispatcher dispatcher)
    {
        _library = library;
        _cache = cache;
        _dispatcher = dispatcher;

        ((INotifyCollectionChanged)_library.Entries).CollectionChanged += OnEntriesChanged;
        foreach (var entry in _library.Entries) Enqueue(entry);

        _worker = Task.Run(ProcessAsync);
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is null) return;
        foreach (AudioFileEntry entry in e.NewItems) Enqueue(entry);
    }

    private void Enqueue(AudioFileEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.Sha256)) return;
        Interlocked.Increment(ref _enqueued);
        PublishProgress();
        _queue.Writer.TryWrite(entry);
    }

    private async Task ProcessAsync()
    {
        var token = _cts.Token;
        try
        {
            while (await _queue.Reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var entry))
                {
                    if (token.IsCancellationRequested) return;
                    try { ProcessEntry(entry); }
                    catch { /* per-file failures are swallowed; entry stays unhashed */ }
                    finally
                    {
                        Interlocked.Increment(ref _completed);
                        PublishProgress();
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ProcessEntry(AudioFileEntry entry)
    {
        var path = entry.AbsolutePath;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var fi = new FileInfo(path);
        long size = fi.Length;
        long mtime = fi.LastWriteTimeUtc.Ticks;

        var hash = _cache.TryGet(path, size, mtime);
        if (hash is null)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                bufferSize: 1 << 16, FileOptions.SequentialScan);
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(stream);
            hash = Convert.ToHexString(bytes);
            _cache.Set(path, hash, size, mtime);
        }

        if (entry.ByteSize == 0) entry.ByteSize = size;
        _library.AssignHash(entry, hash);
    }

    private void PublishProgress()
    {
        int total = Volatile.Read(ref _enqueued);
        int done = Volatile.Read(ref _completed);
        _dispatcher.BeginInvoke(() =>
        {
            TotalCount = total;
            ProcessedCount = done;
            PendingCount = total - done;
            IsScanning = PendingCount > 0;
            OnPropertyChanged(nameof(Progress));
        });
    }

    public void Dispose()
    {
        _cts.Cancel();
        _queue.Writer.TryComplete();
        try { _worker.Wait(500); } catch { }
        _cache.Flush();
    }
}
