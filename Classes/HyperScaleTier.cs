
namespace Azure.SQL.DB.Hyperscale.Tools.Classes
{
    public class HyperScaleTier
    {
        private readonly string Name = "hs";
        public int Generation = 5;
        public int Cores = 4;

        public override string ToString()
        {
            return $"{Name}_gen{Generation}_{Cores}".ToUpper();
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            if (GetType() != obj.GetType())
                return false;

            return ToString() == obj.ToString();
        }

        public override int GetHashCode()
        {
            return ToString().GetHashCode();
        }

        public static bool operator ==(HyperScaleTier lhs, HyperScaleTier rhs)
        {
            if (lhs is null)
            {
                if (rhs is null)
                    return true;

                return false;
            }

            return lhs.Equals(rhs);
        }

        public static bool operator !=(HyperScaleTier lhs, HyperScaleTier rhs)
        {
            return !(lhs == rhs);
        }

        public static HyperScaleTier Parse(string tierName)
        {
            var curName = tierName.ToLower();
            var parts = curName.Split('_');

            if (parts[0] != "hs")
                throw new ArgumentException($"'{tierName}' is not an Hyperscale Tier");

            var result = new HyperScaleTier();
            result.Generation = int.Parse(parts[1].Replace("gen", string.Empty));
            result.Cores = int.Parse(parts[2]);

            return result;
        }
    }

}
