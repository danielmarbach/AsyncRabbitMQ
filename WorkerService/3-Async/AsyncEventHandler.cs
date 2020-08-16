using System;
using System.Threading.Tasks;

namespace WorkerService
{
    public delegate Task AsyncEventHandler<in TEvent>(object sender, TEvent @event) where TEvent : EventArgs;
}