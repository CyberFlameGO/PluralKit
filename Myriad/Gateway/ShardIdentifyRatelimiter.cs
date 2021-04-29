﻿using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

using Serilog;

namespace Myriad.Gateway
{
    public class ShardIdentifyRatelimiter
    {
        private static readonly TimeSpan BucketLength = TimeSpan.FromSeconds(5);

        private readonly ConcurrentDictionary<int, ConcurrentQueue<TaskCompletionSource>> _buckets = new();
        private readonly int _maxConcurrency;

        private Task? _refillTask;
        private readonly ILogger _logger;

        public ShardIdentifyRatelimiter(ILogger logger, int maxConcurrency)
        {
            _logger = logger.ForContext<ShardIdentifyRatelimiter>();
            _maxConcurrency = maxConcurrency;
        }

        public Task Acquire(int shard)
        {
            var bucket = shard % _maxConcurrency;
            var queue = _buckets.GetOrAdd(bucket, _ => new ConcurrentQueue<TaskCompletionSource>());
            var tcs = new TaskCompletionSource();
            queue.Enqueue(tcs);

            ScheduleRefill();

            return tcs.Task;
        }

        private void ScheduleRefill()
        {
            if (_refillTask != null && !_refillTask.IsCompleted)
                return;

            _refillTask?.Dispose();
            _refillTask = RefillTask();
        }

        private async Task RefillTask()
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));

            while (true)
            {
                var isClear = true;
                foreach (var (bucket, queue) in _buckets)
                {
                    if (!queue.TryDequeue(out var tcs))
                        continue;

                    _logger.Information(
                        "Allowing identify for bucket {BucketId} through ({QueueLength} left in bucket queue)",
                        bucket, queue.Count);
                    tcs.SetResult();
                    isClear = false;
                }

                if (isClear)
                    return;

                await Task.Delay(BucketLength);
            }
        }
    }
}