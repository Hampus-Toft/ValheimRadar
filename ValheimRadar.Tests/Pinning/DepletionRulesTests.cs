using System.Collections.Generic;
using UnityEngine;
using Xunit;

namespace ValheimRadar.Tests.Pinning
{
    // Removal of mined-out/depleted resource pins. The scan/Minimap side (PinManager.RecordScannedCells,
    // DepletionPatches) needs a live game, but every decision it takes is made here.
    public class DepletionRulesTests
    {
        private static TrackedItem Found(string categoryKey, Vector3 position) =>
            new TrackedItem { CategoryKey = categoryKey, Position = position };

        [Fact]
        public void IsPresent_SameCategoryAtSamePosition_IsPresent()
        {
            var found = new List<TrackedItem> { Found("resource:CopperDeposit", new Vector3(10f, 50f, 10f)) };
            Assert.True(DepletionRules.IsPresent("resource:CopperDeposit", new Vector3(10.2f, 50f, 10f), found));
        }

        [Fact]
        public void IsPresent_OnlyDifferentCategoryThere_IsMissing()
        {
            var found = new List<TrackedItem> { Found("resource:TinDeposit", new Vector3(10f, 50f, 10f)) };
            Assert.False(DepletionRules.IsPresent("resource:CopperDeposit", new Vector3(10f, 50f, 10f), found));
        }

        [Fact]
        public void IsPresent_SameCategoryElsewhere_IsMissing()
        {
            var found = new List<TrackedItem> { Found("resource:Flint", new Vector3(12f, 50f, 10f)) };
            Assert.False(DepletionRules.IsPresent("resource:Flint", new Vector3(10f, 50f, 10f), found));
        }

        [Fact]
        public void SingleMiss_NeverRemoves()
        {
            var point = new TrackedItem();
            DepletionRules.RecordObservation(point, present: false, now: 100f);
            Assert.False(DepletionRules.ShouldRemove(point, now: 1000f));
        }

        [Fact]
        public void TwoMissesSpanningMinimumTime_Removes()
        {
            var point = new TrackedItem();
            DepletionRules.RecordObservation(point, present: false, now: 100f);
            DepletionRules.RecordObservation(point, present: false, now: 100f + DepletionRules.MinMissSpanSeconds);
            Assert.True(DepletionRules.ShouldRemove(point, now: 100f + DepletionRules.MinMissSpanSeconds));
        }

        // Two back-to-back scans while the server is still streaming a zone must not count as proof.
        [Fact]
        public void TwoMissesTooCloseTogether_DoesNotRemoveYet()
        {
            var point = new TrackedItem();
            DepletionRules.RecordObservation(point, present: false, now: 100f);
            DepletionRules.RecordObservation(point, present: false, now: 101f);
            Assert.False(DepletionRules.ShouldRemove(point, now: 101f));
        }

        [Fact]
        public void SeenAgainBetweenMisses_ResetsStreak()
        {
            var point = new TrackedItem();
            DepletionRules.RecordObservation(point, present: false, now: 100f);
            DepletionRules.RecordObservation(point, present: true, now: 130f);
            DepletionRules.RecordObservation(point, present: false, now: 160f);
            Assert.False(DepletionRules.ShouldRemove(point, now: 160f));
        }

        [Fact]
        public void StreakStartTime_IsTheFirstMissAfterAReset()
        {
            var point = new TrackedItem();
            DepletionRules.RecordObservation(point, present: false, now: 100f);
            DepletionRules.RecordObservation(point, present: true, now: 130f);
            DepletionRules.RecordObservation(point, present: false, now: 160f);
            DepletionRules.RecordObservation(point, present: false, now: 165f);
            Assert.False(DepletionRules.ShouldRemove(point, now: 165f));
        }

        [Theory]
        [InlineData(0f, true)]
        [InlineData(-94f, true)]
        [InlineData(94f, true)]
        [InlineData(-96f, false)]
        [InlineData(96f, false)]
        public void IsWithinVerifiedHeight_ExcludesTheQueryBoxEdges(float y, bool expected)
        {
            Assert.Equal(expected, DepletionRules.IsWithinVerifiedHeight(y, -100f, 100f));
        }
    }
}
