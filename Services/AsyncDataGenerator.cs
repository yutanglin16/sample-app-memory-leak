using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PerformanceIssues.Services
{
    public interface IAsyncDataGenerator
    {
        Task<Guid> GenerateAndStoreDataAsync(int count, CancellationToken cancellationToken = default);
        Task<int> GetStoredDataCountAsync();
        Task<bool> ClearDataAsync();
    }

    public class AsyncDataGenerator : IAsyncDataGenerator
    {
        // Using a concurrent collection for thread safety
        private readonly ConcurrentBag<object> _storedData = new();
        private readonly Random _random = new();
        
        // Track generated data batches with their IDs for potential later retrieval
        private readonly ConcurrentDictionary<Guid, int> _generationBatches = new();

        // Configurable generation parameters
        private readonly int _recordSizeKb;
        private readonly int _delayPerRecordMs;

        public AsyncDataGenerator(int recordSizeKb = 1, int delayPerRecordMs = 1)
        {
            _recordSizeKb = Math.Max(1, recordSizeKb); // Minimum 1KB
            _delayPerRecordMs = Math.Max(0, delayPerRecordMs);
        }

        public async Task<Guid> GenerateAndStoreDataAsync(int count, CancellationToken cancellationToken = default)
        {
            // Create a batch ID to track this generation
            var batchId = Guid.NewGuid();
            _generationBatches[batchId] = count;
            
            // Use Task.Run to move the CPU-bound work to a background thread
            await Task.Run(async () =>
            {
                for (int i = 0; i < count; i++)
                {
                    // Check for cancellation
                    cancellationToken.ThrowIfCancellationRequested();
                    
                    var data = new
                    {
                        Id = Guid.NewGuid(),
                        BatchId = batchId,
                        Name = await GenerateRandomStringAsync(50, cancellationToken),
                        Value = _random.Next(1, 1000000),
                        Timestamp = DateTime.UtcNow,
                        Data = new byte[_recordSizeKb * 1024] // Configurable size
                    };
                    
                    // Generate random bytes asynchronously
                    await Task.Run(() => _random.NextBytes(data.Data), cancellationToken);
                    
                    // Add to our leaking collection
                    _storedData.Add(data);
                    
                    // Configurable delay to simulate work
                    if (_delayPerRecordMs > 0)
                    {
                        await Task.Delay(_delayPerRecordMs, cancellationToken);
                    }
                }
            }, cancellationToken);
            
            return batchId;
        }

        public Task<int> GetStoredDataCountAsync()
        {
            // Quickly return the count without blocking
            return Task.FromResult(_storedData.Count);
        }

        public Task<bool> ClearDataAsync()
        {
            // In a real implementation, we might want to keep this for debugging
            // But for demo purposes, allow clearing the memory leak
            while (!_storedData.IsEmpty)
            {
                _storedData.TryTake(out _);
            }
            _generationBatches.Clear();
            
            return Task.FromResult(true);
        }

        private async Task<string> GenerateRandomStringAsync(int length, CancellationToken cancellationToken)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            
            // Move string generation to background thread to avoid blocking
            return await Task.Run(() =>
            {
                var stringBuilder = new StringBuilder(length);
                for (int i = 0; i < length; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    stringBuilder.Append(chars[_random.Next(chars.Length)]);
                }
                return stringBuilder.ToString();
            }, cancellationToken);
        }
    }

    public interface IAsyncLeakyCache
    {
        Task<string> AddToCacheAsync(string key, int sizeInMb, CancellationToken cancellationToken = default);
        Task<int> GetCacheSizeAsync();
        Task<long> GetCacheMemoryUsageMbAsync();
        Task<bool> TryRemoveFromCacheAsync(string key);
    }

    public class AsyncLeakyCache : IAsyncLeakyCache
    {
        private static readonly ConcurrentDictionary<string, byte[]> _cache = new();
        private static readonly Random _random = new();
        
        public async Task<string> AddToCacheAsync(string key, int sizeInMb, CancellationToken cancellationToken = default)
        {
            // Create large memory allocation asynchronously
            var data = await Task.Run(() =>
            {
                var buffer = new byte[sizeInMb * 1024 * 1024];
                _random.NextBytes(buffer);
                return buffer;
            }, cancellationToken);
            
            // Simulate some async work like network or disk I/O
            await Task.Delay(100, cancellationToken);
            
            // Add to cache with memory leak (no eviction policy)
            _cache.TryAdd(key, data);
            
            return key;
        }
        
        public Task<int> GetCacheSizeAsync()
        {
            return Task.FromResult(_cache.Count);
        }
        
        public Task<long> GetCacheMemoryUsageMbAsync()
        {
            // Calculate approximate memory usage
            long totalBytes = _cache.Sum(kvp => kvp.Value.Length);
            return Task.FromResult(totalBytes / (1024 * 1024));
        }
        
        public Task<bool> TryRemoveFromCacheAsync(string key)
        {
            // For demonstration purposes, allow removing items
            bool result = _cache.TryRemove(key, out _);
            return Task.FromResult(result);
        }
    }

    public interface IAsyncEventManager
    {
        Task SubscribeAsync(Func<string, Task> handler);
        Task RaiseEventAsync(string message, CancellationToken cancellationToken = default);
        Task<int> GetSubscriberCountAsync();
    }

    public class AsyncEventManager : IAsyncEventManager
    {
        // Using concurrent collection for thread safety
        private readonly ConcurrentBag<Func<string, Task>> _subscribers = new();
        
        // Intentional memory leak - subscribers are never removed
        public Task SubscribeAsync(Func<string, Task> handler)
        {
            _subscribers.Add(handler);
            return Task.CompletedTask;
        }
        
        public async Task RaiseEventAsync(string message, CancellationToken cancellationToken = default)
        {
            // Create a copy to avoid potential enumeration issues
            var subscribers = _subscribers.ToArray();
            
            // Process each subscriber in parallel for better performance
            var tasks = subscribers.Select(handler => 
                Task.Run(async () => 
                {
                    try
                    {
                        await handler(message);
                    }
                    catch (Exception ex)
                    {
                        // In a real implementation, we'd log this
                        Console.WriteLine($"Error in event handler: {ex.Message}");
                    }
                }, cancellationToken)
            );
            
            // Wait for all notifications to complete
            await Task.WhenAll(tasks);
        }
        
        public Task<int> GetSubscriberCountAsync()
        {
            return Task.FromResult(_subscribers.Count);
        }
    }
}
