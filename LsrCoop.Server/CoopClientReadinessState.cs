namespace LsrCoop.Server
{
    internal enum CoopClientReadinessState
    {
        Connected = 0,
        Compatible = 1,
        ProfileLoaded = 2,
        CharacterRequired = 3,
        CharacterSnapshotSent = 4,
        CharacterReadyForSimulation = 5,
    }
}
