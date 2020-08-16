using System.Threading.Tasks;

namespace WorkerService
{
    public static class TaskExtensions
    {
        public static Task CompletedTask = Task.FromResult(0);
    }
}