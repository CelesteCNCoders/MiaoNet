using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Celeste.Mod.MiaoNet;

internal sealed class SingleThreadedTaskScheduler : TaskScheduler
{
    private readonly SynchronizationContext syncCtx;

    public SingleThreadedTaskScheduler(SingleThreadedSynchronizationContext syncCtx)
    {
        this.syncCtx = syncCtx;
    }

    protected override void QueueTask(Task task)
    {
        syncCtx.Post(state => TryExecuteTask((Task)state!), task);
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) 
        => SynchronizationContext.Current == syncCtx && TryExecuteTask(task);

    protected override IEnumerable<Task> GetScheduledTasks() => [];
}
