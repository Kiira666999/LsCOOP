namespace LosSantosRED.lsr.Coop.Core
{
    public interface ICoopEventSerializer
    {
        byte[] Serialize(CoopEventEnvelope envelope);
        CoopEventEnvelope Deserialize(byte[] payload);
    }
}
