﻿using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using WorkerService.Async;

namespace WorkerService.Cleanup
{
    internal sealed class AsyncConsumerWorkService : ConsumerWorkService
    {
        private readonly ConcurrentDictionary<IModel, WorkPool> _workPools = new ConcurrentDictionary<IModel, WorkPool>();

        public void Schedule<TWork>(ModelBase model, TWork work) where TWork : Work
        {
            _workPools.GetOrAdd(model, StartNewWorkPool).Enqueue(work);
        }

        private WorkPool StartNewWorkPool(IModel model)
        {
            var newWorkPool = new WorkPool(model as ModelBase);
            newWorkPool.Start();
            return newWorkPool;
        }

        public Task Stop(IModel model)
        {
            if (_workPools.TryRemove(model, out WorkPool workPool))
            {
                return workPool.Stop();
            }

            return Task.CompletedTask;
        }

        class WorkPool
        {
            readonly ConcurrentQueue<Work> _workQueue;
            readonly CancellationTokenSource _tokenSource;
            readonly ModelBase _model;
            readonly CancellationTokenRegistration _tokenRegistration;
            volatile TaskCompletionSource<bool> _syncSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private Task _worker;

            // readonly SemaphoreSlim _semaphore = new SemaphoreSlim(0);

            public WorkPool(ModelBase model)
            {
                _model = model;
                _workQueue = new ConcurrentQueue<Work>();
                _tokenSource = new CancellationTokenSource();
                _tokenRegistration = _tokenSource.Token.Register(() => _syncSource.TrySetCanceled());
            }

            public void Start()
            {
                _worker = Task.Run(Loop, CancellationToken.None);
            }

            public void Enqueue(Work work)
            {
                _workQueue.Enqueue(work);
                _syncSource.TrySetResult(true);

                // _semaphore.Release();
            }

            async Task Loop()
            {
                while (_tokenSource.IsCancellationRequested == false)
                {
                    try
                    {
                        // await _semaphore.WaitAsync(_tokenSource.Token).ConfigureAwait(false);

                        await _syncSource.Task.ConfigureAwait(false);
                        _syncSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    catch (TaskCanceledException)
                    {
                        // Swallowing the task cancellation in case we are stopping work.
                    }

                    while (_workQueue.TryDequeue(out Work work))
                    {
                        await work.Execute(_model).ConfigureAwait(false);
                    }
                }
            }

            public Task Stop()
            {
                _tokenSource.Cancel();
                _tokenRegistration.Dispose();
                return _worker;
            }
        }
    }
}