#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VRC.SDK3.Validation;

namespace THE_cattail.AfkMotionPatcher.Tests
{
    public sealed class AfkMotionPatchValidationTests
    {
        [Test]
        public void AuthoringComponent_IsEditorOnlyAndNotRestrictedByVrchatValidation()
        {
            var owner = new GameObject("AFK Motion Patch validation test");
            try
            {
                var patch = owner.AddComponent<AfkMotionPatch>();

                Assert.That(typeof(AfkMotionPatch).GetInterfaces()
                    .Any(type => type.FullName == "nadena.dev.ndmf.INDMFEditorOnly"), Is.True);
                Assert.That(AvatarValidation.FindIllegalComponents(owner).Contains(patch), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
#endif
