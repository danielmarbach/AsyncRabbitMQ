using System.Threading.Tasks;

namespace WorkerService
{
    class Work
    {
        public Task Execute()
        {
            return Task.CompletedTask;
        }

        public Task Execute(ModelBase modelBase)
        {
            return Task.CompletedTask;
        }

        public void PostExecute()
        {
        }
    }
}