namespace LsrCoop.Server
{
    public class CoopLastPositionStateDto
    {
        public string WorldId { get; set; }
        public string ProfileId { get; set; }
        public string CharacterId { get; set; }
        public string SessionId { get; set; }
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Heading { get; set; }
        public long UpdatedUtcUnixMilliseconds { get; set; }
        public string Source { get; set; }
    }
}
