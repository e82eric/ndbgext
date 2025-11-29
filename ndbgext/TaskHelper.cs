using System.Diagnostics.CodeAnalysis;
using Microsoft.Diagnostics.Runtime;

namespace ndbgext;

public static class TaskHelper
{
    public static bool TryGetTaskItem(ClrRuntime runtime, ClrObject heapObject, [NotNullWhen(true)] out TasksItem? item)
    {
        item = null;
        if (heapObject.Type == null || heapObject.Type.Name == null)
        {
            return false;
        }

        if (heapObject.TryReadValueTypeField("m_taskId", out var _))
        {
            if(heapObject.TryReadValueTypeField("m_stateFlags", out var _))
            {
                var stateFlags = heapObject.ReadField<ulong>("m_stateFlags");
                item = new TasksItem
                {
                    TaskName = heapObject.Type.Name,
                    Address = heapObject.Address,
                    TaskState = GetTaskState(stateFlags),
                    Method = Helper.GetDelegateMethod(runtime, heapObject)
                };

                if (heapObject.TryReadObjectField("m_stateObject", out var stateObject))
                {
                    if (!stateObject.IsNull && stateObject.IsValid && stateObject.Type != null)
                    {
                        item.StateMachine = stateObject.Type.Name;
                    }
                }
                        
                if (heapObject.TryReadObjectField("m_continuationObject", out var continuationObject))
                {
                    if (!continuationObject.IsNull && continuationObject.IsValid && continuationObject.Type != null)
                    {
                        if (continuationObject.TryReadObjectField("StateMachine", out var stateMachine))
                        {
                            item.ContinuationStateMachine = stateMachine.Type?.Name;
                        }
                        else if (continuationObject.TryReadObjectField("_target", out var target))
                        {
                            if (target.TryReadObjectField("m_stateMachine", out var stateMachine2))
                            {
                                item.ContinuationStateMachine = stateMachine2.Type?.Name;
                            }
                        }
                        else
                        {
                            item.ContinuationStateMachine = continuationObject.Type.Name;
                        }
                    }
                }

                return true;
            }
        }

        item = null;
        return false;
    }
    
    private static string GetTaskState(ulong flag)
    {
        TaskStatus result;

        if ((flag & TASK_STATE_FAULTED) != 0) result = TaskStatus.Faulted;
        else if ((flag & TASK_STATE_CANCELED) != 0) result = TaskStatus.Canceled;
        else if ((flag & TASK_STATE_RAN_TO_COMPLETION) != 0) result = TaskStatus.RanToCompletion;
        else if ((flag & TASK_STATE_WAITING_ON_CHILDREN) != 0) result = TaskStatus.WaitingForChildrenToComplete;
        else if ((flag & TASK_STATE_DELEGATE_INVOKED) != 0) result = TaskStatus.Running;
        else if ((flag & TASK_STATE_STARTED) != 0) result = TaskStatus.WaitingToRun;
        else if ((flag & TASK_STATE_WAITINGFORACTIVATION) != 0) result = TaskStatus.WaitingForActivation;
        else if (flag == 0) result = TaskStatus.Created;
        else return $"Unknown (0x{flag:X})";

        return result.ToString();
    }

    // from CLR implementation
    internal const int TASK_STATE_STARTED                       =      65536;
    internal const int TASK_STATE_DELEGATE_INVOKED              =     131072;
    internal const int TASK_STATE_DISPOSED                      =     262144;
    internal const int TASK_STATE_EXCEPTIONOBSERVEDBYPARENT     =     524288;
    internal const int TASK_STATE_CANCELLATIONACKNOWLEDGED      =    1048576;
    internal const int TASK_STATE_FAULTED                       =    2097152;
    internal const int TASK_STATE_CANCELED                      =    4194304;
    internal const int TASK_STATE_WAITING_ON_CHILDREN           =    8388608;
    internal const int TASK_STATE_RAN_TO_COMPLETION             =   16777216;
    internal const int TASK_STATE_WAITINGFORACTIVATION          =   33554432;
    internal const int TASK_STATE_COMPLETION_RESERVED           =   67108864;
    internal const int TASK_STATE_THREAD_WAS_ABORTED            =  134217728;
    internal const int TASK_STATE_WAIT_COMPLETION_NOTIFICATION  =  268435456;
    internal const int TASK_STATE_EXECUTIONCONTEXT_IS_NULL      =  536870912;
    internal const int TASK_STATE_TASKSCHEDULED_WAS_FIRED       = 1073741824;
}