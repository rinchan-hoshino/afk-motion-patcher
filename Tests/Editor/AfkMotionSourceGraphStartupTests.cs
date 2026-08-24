#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor.Animations;
using UnityEngine;

namespace THE_cattail.AfkMotionPatcher.Tests
{
    public sealed class AfkMotionSourceGraphStartupTests
    {
        private AnimatorController _controller;

        [TearDown]
        public void TearDown()
        {
            if (_controller) Object.DestroyImmediate(_controller);
        }

        [Test]
        public void ExtractedGraphKeepsNeutralDefaultAndOnlyItsAfkEntryTransition()
        {
            _controller = new AnimatorController { name = "SyntheticAction" };
            _controller.AddParameter(AfkMotionSourceGraph.AfkParameterName, AnimatorControllerParameterType.Bool);
            _controller.AddParameter("VRCEmote", AnimatorControllerParameterType.Int);

            var root = new AnimatorStateMachine { name = "Action" };
            var layer = new AnimatorControllerLayer
            {
                name = "Action",
                stateMachine = root,
                defaultWeight = 0f
            };
            _controller.AddLayer(layer);

            var wait = root.AddState("WaitForActionOrAFK");
            var prepare = root.AddState("Prepare AFK");
            var intro = root.AddState("AFK Intro");
            var loop = root.AddState("AFK Loop");
            var outro = root.AddState("AFK Outro");
            var restore = root.AddState("Restore Tracking AFK");
            var unrelated = root.AddState("Wave");
            root.defaultState = wait;

            var afkEntry = wait.AddTransition(prepare);
            afkEntry.hasExitTime = false;
            afkEntry.AddCondition(AnimatorConditionMode.If, 0f, AfkMotionSourceGraph.AfkParameterName);
            var unrelatedEntry = wait.AddTransition(unrelated);
            unrelatedEntry.hasExitTime = false;
            unrelatedEntry.AddCondition(AnimatorConditionMode.Equals, 1f, "VRCEmote");
            prepare.AddTransition(intro).hasExitTime = true;
            intro.AddTransition(loop).hasExitTime = true;
            var leave = loop.AddTransition(outro);
            leave.hasExitTime = false;
            leave.AddCondition(AnimatorConditionMode.IfNot, 0f, AfkMotionSourceGraph.AfkParameterName);
            outro.AddTransition(restore).hasExitTime = true;
            restore.AddExitTransition().hasExitTime = true;

            Assert.That(AfkMotionSourceGraph.TryExtract(_controller, out var graph, out var error), Is.True, error);
            var extracted = graph.Layers.Single();

            Assert.That(extracted.NeutralState, Is.SameAs(wait),
                "AFK=false must start in the source controller's neutral default state.");
            Assert.That(extracted.States.Contains(wait), Is.True);
            Assert.That(extracted.Transitions.Contains(afkEntry), Is.True);
            Assert.That(extracted.Transitions.Contains(unrelatedEntry), Is.False);
            Assert.That(extracted.States.Contains(unrelated), Is.False);
        }
    }
}
#endif
