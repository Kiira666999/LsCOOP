using System;

namespace LosSantosRED.lsr.Coop.Core
{
    public struct CoopCharacterId : IEquatable<CoopCharacterId>
    {
        public CoopCharacterId(string value)
        {
            Value = value;
        }

        public string Value { get; private set; }
        public bool IsEmpty => string.IsNullOrWhiteSpace(Value);

        public bool Equals(CoopCharacterId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CoopCharacterId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : Value.GetHashCode();
        public override string ToString() => Value ?? string.Empty;
    }
}
