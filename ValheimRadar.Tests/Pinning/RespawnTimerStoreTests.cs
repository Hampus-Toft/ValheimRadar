using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Pinning
{
    public class RespawnTimerStoreTests
    {
        private const string Raspberry = "resource:Raspberry";

        [Fact]
        public void GetRespawnAt_AddsMinutesAsSeconds()
        {
            Assert.Equal(1000.0 + 300 * 60, RespawnTimerStore.GetRespawnAt(1000.0, 300f));
        }

        [Fact]
        public void Contains_WithinMatchRadiusAndSameCategory_ReturnsTrue()
        {
            var store = new RespawnTimerStore();
            store.Add(Raspberry, new Vector3(10, 5, 20), 0, 100);

            Assert.True(store.Contains(Raspberry, new Vector3(10.2f, 99, 20.2f)));
            Assert.False(store.Contains(Raspberry, new Vector3(11, 5, 20)));
            Assert.False(store.Contains("resource:Blueberry", new Vector3(10, 5, 20)));
        }

        [Fact]
        public void Add_SamePoint_RestartsTimerInsteadOfDuplicating()
        {
            var store = new RespawnTimerStore();
            store.Add(Raspberry, new Vector3(10, 5, 20), 0, 100);
            store.Add(Raspberry, new Vector3(10.1f, 5, 20), 50, 150);

            Assert.Equal(1, store.Count);
            Assert.Null(store.TakeExpired(120));
            Assert.Single(store.TakeExpired(150));
        }

        [Fact]
        public void TakeExpired_BeforeRespawnTime_KeepsEntry()
        {
            var store = new RespawnTimerStore();
            store.Add(Raspberry, Vector3.zero, 0, 100);

            Assert.Null(store.TakeExpired(99.9));
            Assert.True(store.Contains(Raspberry, Vector3.zero));
        }

        [Fact]
        public void TakeExpired_AtRespawnTime_RemovesAndReturnsEntry()
        {
            var store = new RespawnTimerStore();
            store.Add(Raspberry, new Vector3(1, 2, 3), 0, 100);
            store.Add(Raspberry, new Vector3(50, 2, 3), 0, 500);

            var expired = store.TakeExpired(100);

            Assert.Single(expired);
            Assert.Equal(new Vector3(1, 2, 3), expired[0].Position);
            Assert.False(store.Contains(Raspberry, new Vector3(1, 2, 3)));
            Assert.True(store.Contains(Raspberry, new Vector3(50, 2, 3)));
        }

        // World time only goes backwards when the world was replaced/reset under the same name.
        [Fact]
        public void TakeExpired_WorldTimeBeforePickTime_ExpiresEntry()
        {
            var store = new RespawnTimerStore();
            store.Add(Raspberry, Vector3.zero, 5000, 6000);

            Assert.Single(store.TakeExpired(10));
            Assert.Equal(0, store.Count);
        }

        [Fact]
        public void SerializeThenLoad_RoundTripsEntries()
        {
            var store = new RespawnTimerStore();
            store.Add("resource:Cloud|berry", new Vector3(1.5f, -2.25f, 3.125f), 123456.789, 141456.789);

            var loaded = new RespawnTimerStore();
            loaded.Load(store.Serialize());

            Assert.Equal(1, loaded.Count);
            Assert.True(loaded.Contains("resource:Cloud|berry", new Vector3(1.5f, -2.25f, 3.125f)));
            Assert.Null(loaded.TakeExpired(141456.788));
            Assert.Single(loaded.TakeExpired(141456.789));
        }

        [Fact]
        public void Load_SkipsMalformedLines()
        {
            var store = new RespawnTimerStore();
            store.Load(new[] { "#respawnv1", "", "too|few", "resource%3ARaspberry|x|0|0|0|1", "resource%3ARaspberry|1|2|3|0|100" });

            Assert.Equal(1, store.Count);
            Assert.True(store.Contains(Raspberry, new Vector3(1, 2, 3)));
        }
    }
}
