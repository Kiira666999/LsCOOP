using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public interface ICoopTransport
    {
        event Action<CoopEventEnvelope> EventReceived;

        CoopTransportState State { get; }
        bool IsEnabled { get; }

        void Start();
        void Stop();
        void Send(CoopEventEnvelope envelope);
    }
}
