using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PerformanceIssues.Models;
using PerformanceIssues.Services;

namespace PerformanceIssuesDemo.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MemoryController : ControllerBase
    {
        private readonly IAsyncLeakyCache _leakyCache;
        private readonly IAsyncEventManager _eventManager;
        private readonly IAsyncDataGenerator _dataGenerator;
        private readonly ILogger<MemoryController> _logger;

        public MemoryController(
            IAsyncLeakyCache leakyCache,
            IAsyncEventManager eventManager,
            IAsyncDataGenerator dataGenerator,
            ILogger<MemoryController> logger)
        {
            _leakyCache = leakyCache;
            _eventManager = eventManager;
            _dataGenerator = dataGenerator;
            _logger = logger;
        }

        [HttpPost("cache")]
        public async Task<IActionResult> AddToCache([FromBody] CacheEntryRequest request, CancellationToken cancellationToken)
        {
            if (request.SizeMB <= 0 || request.SizeMB > 1000)
                return BadRequest("Size must be between 1 and 1000 MB");

            try
            {
                var key = await _leakyCache.AddToCacheAsync(Guid.NewGuid().ToString(), request.SizeMB, cancellationToken);
                var cacheSize = await _leakyCache.GetCacheSizeAsync();
                var memoryUsage = await _leakyCache.GetCacheMemoryUsageMbAsync();
                
                return Ok(new { key, size = request.SizeMB, totalEntries = cacheSize, totalMemoryMb = memoryUsage });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, "Request was canceled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding to cache");
                return StatusCode(500, "An error occurred while adding to cache");
            }
        }

        [HttpGet("cache/size")]
        public async Task<IActionResult> GetCacheSize(CancellationToken cancellationToken)
        {
            try
            {
                var count = await _leakyCache.GetCacheSizeAsync();
                var memoryUsage = await _leakyCache.GetCacheMemoryUsageMbAsync();
                
                return Ok(new { entries = count, memoryUsageMb = memoryUsage });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cache size");
                return StatusCode(500, "An error occurred while retrieving cache size");
            }
        }

        [HttpPost("subscribe")]
        public async Task<IActionResult> Subscribe(CancellationToken cancellationToken)
        {
            try
            {
                var id = Guid.NewGuid().ToString();
                
                // Create a memory-leaking subscriber that captures the id variable
                await _eventManager.SubscribeAsync(async message => 
                {
                    await Task.Delay(10); // Simulate some async work
                    _logger.LogInformation("Event received for {Id}: {Message}", id, message);
                });
                
                // Raise a test event
                await _eventManager.RaiseEventAsync($"Test event for {id}", cancellationToken);
                
                var subscriberCount = await _eventManager.GetSubscriberCountAsync();
                return Ok(new { subscriberId = id, totalSubscribers = subscriberCount });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, "Request was canceled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error subscribing to events");
                return StatusCode(500, "An error occurred while subscribing");
            }
        }

        [HttpPost("generate-data")]
        public async Task<IActionResult> GenerateData([FromBody] DataGenerationRequest request, CancellationToken cancellationToken)
        {
            if (request.RecordCount <= 0 || request.RecordCount > 1000000)
                return BadRequest("Record count must be between 1 and 1,000,000");

            try
            {
                var batchId = await _dataGenerator.GenerateAndStoreDataAsync(request.RecordCount, cancellationToken);
                var totalCount = await _dataGenerator.GetStoredDataCountAsync();
                
                return Ok(new { 
                    batchId = batchId,
                    recordsGenerated = request.RecordCount,
                    totalRecords = totalCount
                });
            }
            catch (OperationCanceledException)
            {
                return StatusCode(499, "Request was canceled");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating data");
                return StatusCode(500, "An error occurred while generating data");
            }
        }

        [HttpPost("generate-data-background")]
        public IActionResult GenerateDataBackground([FromBody] DataGenerationRequest request)
        {
            if (request.RecordCount <= 0 || request.RecordCount > 1000000)
                return BadRequest("Record count must be between 1 and 1,000,000");

            // Use a linked cancellation token source that will be canceled after 10 minutes
            // to prevent infinite background tasks
            var cts = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            
            // Start generating data on a background thread (fire-and-forget)
            // In a production app, you'd want to use a proper background job system
            _ = Task.Run(async () => 
            {
                try 
                {
                    var batchId = await _dataGenerator.GenerateAndStoreDataAsync(request.RecordCount, cts.Token);
                    _logger.LogInformation("Background generation completed: {BatchId}, {RecordCount} records", 
                        batchId, request.RecordCount);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("Background data generation was canceled");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in background data generation");
                }
                finally
                {
                    cts.Dispose();
                }
            });

            // Immediately return a response, so the caller isn't blocked
            return Accepted(new { 
                recordsRequested = request.RecordCount,
                status = "Background processing started"
            });
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetMemoryStats(CancellationToken cancellationToken)
        {
            try
            {
                var cacheSize = await _leakyCache.GetCacheSizeAsync();
                var cacheMemory = await _leakyCache.GetCacheMemoryUsageMbAsync();
                var recordCount = await _dataGenerator.GetStoredDataCountAsync();
                var subscriberCount = await _eventManager.GetSubscriberCountAsync();
                
                return Ok(new
                {
                    cacheEntries = cacheSize,
                    cacheMemoryMb = cacheMemory,
                    storedRecords = recordCount,
                    eventSubscribers = subscriberCount,
                    processMemoryMb = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving memory stats");
                return StatusCode(500, "An error occurred while retrieving memory stats");
            }
        }

        [HttpPost("clear-data")]
        public async Task<IActionResult> ClearData(CancellationToken cancellationToken)
        {
            try
            {
                await _dataGenerator.ClearDataAsync();
                return Ok(new { status = "Data cleared" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing data");
                return StatusCode(500, "An error occurred while clearing data");
            }
        }
    }
}
