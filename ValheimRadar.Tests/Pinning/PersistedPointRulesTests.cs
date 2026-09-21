using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Pinning
{
    // Issue #42 - persisted resource pins vanishing after a reconnect to a dedicated server. The
    // I/O-heavy path (PinManager.LoadWorldPins) can't run under xunit, but the identity decisions it
    // now relies on are pure and covered here.
    public class PersistedPointRulesTests
    {
        [Fact]
        public void IsDuplicate_SameNameWithinRadius_IsSameObject()
        {
            Assert.True(PersistedPointRules.IsDuplicate("Tin Deposit", new Vector3(10f, 5f, 10f), "Tin Deposit", new Vector3(10.1f, 5f, 10.1f)));
        }

        [Fact]
        public void IsDuplicate_SameNameBeyondRadius_IsDistinctObject()
        {
            Assert.False(PersistedPointRules.IsDuplicate("Tin Deposit", new Vector3(10f, 5f, 10f), "Tin Deposit", new Vector3(12f, 5f, 10f)));
        }

        [Fact]
        public void IsDuplicate_DifferentNameAtSamePosition_IsDistinctObject()
        {
            Assert.False(PersistedPointRules.IsDuplicate("Tin Deposit", Vector3.zero, "Copper Deposit", Vector3.zero));
        }

        [Fact]
        public void ResolveKey_FreeKey_UsesKey()
        {
            Assert.Equal(PersistedPointRules.KeyResolution.UseKey,
                PersistedPointRules.ResolveKey(false, default, new Vector3(1f, 2f, 3f)));
        }

        [Fact]
        public void ResolveKey_TakenBySameObject_IsAlreadyKnown()
        {
            Assert.Equal(PersistedPointRules.KeyResolution.AlreadyKnown,
                PersistedPointRules.ResolveKey(true, new Vector3(1f, 2f, 3f), new Vector3(1.05f, 2f, 3f)));
        }

        // ZDOIDs are re-assigned whenever a server reloads its world (ZDO.Load), so a key match with
        // a different position is a different physical object that must NOT be skipped as "known".
        [Fact]
        public void ResolveKey_TakenByDifferentObject_IsKeyCollision()
        {
            Assert.Equal(PersistedPointRules.KeyResolution.KeyCollision,
                PersistedPointRules.ResolveKey(true, new Vector3(1f, 2f, 3f), new Vector3(400f, 20f, -90f)));
        }

        [Fact]
        public void DisambiguateKey_IsDeterministicAndDistinctFromPlainKeys()
        {
            var pos = new Vector3(123.45f, 6.7f, -89.01f);
            string a = PersistedPointRules.DisambiguateKey("0:42", pos);
            string b = PersistedPointRules.DisambiguateKey("0:42", pos);

            Assert.Equal(a, b);
            Assert.NotEqual("0:42", a);
            Assert.StartsWith("0:42@", a);
        }

        [Fact]
        public void DisambiguateKey_DifferentPositionsGiveDifferentKeys()
        {
            Assert.NotEqual(
                PersistedPointRules.DisambiguateKey("0:42", new Vector3(10f, 0f, 10f)),
                PersistedPointRules.DisambiguateKey("0:42", new Vector3(200f, 0f, 10f)));
        }
    }
}
