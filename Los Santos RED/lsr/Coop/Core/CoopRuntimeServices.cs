namespace LosSantosRED.lsr.Coop.Core
{
    public static class CoopRuntimeServices
    {
        static CoopRuntimeServices()
        {
            ResetToDisabled();
        }

        public static ICoopTransport Transport { get; private set; }
        public static CoopSaveService SaveService { get; private set; }

        public static void Configure(ICoopTransport transport, CoopSaveService saveService)
        {
            Transport = transport ?? new NullCoopTransport();
            SaveService = saveService ?? new NullCoopSaveService();
        }

        public static void ResetToDisabled()
        {
            Transport = new NullCoopTransport();
            SaveService = new NullCoopSaveService();
        }
    }
}
