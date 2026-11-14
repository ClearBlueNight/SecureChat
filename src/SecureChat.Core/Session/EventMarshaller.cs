namespace SecureChat.Core.Session;

// Sends calls to UI context, otherwise it goes on current thread
internal sealed class EventMarshaller(SynchronizationContext? context)
{
    public void Post(Action action)
    {
        if(context is null) action();
        else context.Post(static state => ((Action)state!)(), action);
    }
}