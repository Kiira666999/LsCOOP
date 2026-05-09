using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public class NullCoopTransport : ICoopTransport
    {
        public event Action<CoopEventEnvelope> EventReceived;

        public CoopTransportState State => CoopTransportState.Disabled;
        public bool IsEnabled => false;

        public void Start()
        {
        }

        public void Stop()
        {
        }

        public void Send(CoopEventEnvelope envelope)
        {
        }

        protected void OnEventReceived(CoopEventEnvelope envelope)
        {
            EventReceived?.Invoke(envelope);
        }
    }
}
