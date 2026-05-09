using System;
using System.Reflection;

namespace LsrCoop.Server
{
    internal sealed class ConnectionEventSubscription
    {
        public ConnectionEventSubscription(object source, EventInfo eventInfo, Delegate handler)
        {
            Source = source;
            EventInfo = eventInfo;
            Handler = handler;
        }

        public object Source { get; }
        public EventInfo EventInfo { get; }
        public Delegate Handler { get; }

        public void Unsubscribe()
        {
            EventInfo.RemoveEventHandler(Source, Handler);
        }
    }
}
