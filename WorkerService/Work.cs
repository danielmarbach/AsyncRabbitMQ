using System.Threading.Tasks;

namespace WorkerService
{
    class Work
    {
        public Task Execute(ModelBase modelBase)
        {
            return Task.CompletedTask;
        }
    }
}