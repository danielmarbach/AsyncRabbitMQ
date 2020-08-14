using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace WorkerService.Start
{
    // https://github.com/rabbitmq/rabbitmq-dotnet-client/issues/251
    // https://particular.net/blog/rabbitmq-updates-in-nservicebus-6
    class ConsumerWorkService
    {
        public const int MAX_THUNK_EXECUTION_BATCH_SIZE = 16;
        private TaskScheduler scheduler;
        private BatchingWorkPool<IModel, Action> workPool;

        public ConsumerWorkService() :
            this(TaskScheduler.Default) { }

        public ConsumerWorkService(TaskScheduler scheduler)
        {
            this.scheduler = scheduler;
            this.workPool = new BatchingWorkPool<IModel, Action>();
        }

        public void ExecuteThunk()
        {
            var actions = new List<Action>(MAX_THUNK_EXECUTION_BATCH_SIZE);

            try
            {
                IModel key = this.workPool.NextWorkBlock(ref actions, MAX_THUNK_EXECUTION_BATCH_SIZE);
                if (key == null) { return; }

                try
                {
                    foreach (var fn in actions)
                    {
                        fn();
                    }
                }
                finally
                {
                    if (this.workPool.FinishWorkBlock(key))
                    {
                        var t = new Task(new Action(ExecuteThunk));
                        t.Start(this.scheduler);
                    }
                }
            }
            catch (Exception)
            {
#if NETFX_CORE
                // To end a task, return
                return;
#else
                //Thread.CurrentThread.Interrupt(); //TODO: what to do?
#endif
            }
        }

        public void AddWork(IModel model, Action fn)
        {
            if (this.workPool.AddWorkItem(model, fn))
            {
                var t = new Task(new Action(ExecuteThunk));
                t.Start(this.scheduler);
            }
        }

        public void RegisterKey(IModel model)
        {
            this.workPool.RegisterKey(model);
        }

        public void StopWork(IModel model)
        {
            this.workPool.UnregisterKey(model);
        }

        public void StopWork()
        {
            this.workPool.UnregisterAllKeys();
        }
    }
}