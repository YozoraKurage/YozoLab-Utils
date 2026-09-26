using nadena.dev.ndmf.animator;
using NUnit.Framework;
using UnityEditor.Animations;
using YozoLab.SPS.Menu;
using YozoLab.SPS.Ndmf;

namespace YozoLab.SPS.Tests {
    /**
     * 移植にあたって書き足した部分のテスト（VRCFury 由来のテストは SpsPatcherTest など）。
     */
    [Category("YozoLab SPS")]
    public class SpsPortTests {
        // ---- VRCFury からの移し替え ----------------------------------------

        [Test]
        public void Migration_RemapsSerializeReferenceTypes() {
            var json = "{\"references\":{\"version\":2,\"RefIds\":[{\"rid\":1000,\"type\":"
                       + "{\"class\":\"ObjectToggleAction\",\"ns\":\"VF.Model.StateAction\",\"asm\":\"VRCFury\"},\"data\":{}}]}}";
            var remapped = VRCFuryMigrationMenuItem.RemapTypes(json);
            StringAssert.Contains("\"ns\":\"YozoLab.SPS.Model.StateAction\"", remapped);
            StringAssert.Contains("\"asm\":\"YozoLab.SPS.Runtime\"", remapped);
            StringAssert.DoesNotContain("VRCFury", remapped);
        }

        [Test]
        public void Migration_DoesNotCopyScriptReference() {
            // 写すと、移し替え先のスクリプト参照が VRCFury のものに書き換わってしまう
            var json = "{\"MonoBehaviour\":{\"m_Enabled\":1,\"m_Script\":{\"instanceID\":123},\"length\":0.2}}";
            var remapped = VRCFuryMigrationMenuItem.RemapTypes(json);
            StringAssert.DoesNotContain("m_Script", remapped);
            StringAssert.Contains("\"length\":0.2", remapped);
        }

        // ---- Write Defaults（Modular Avatar の Merge Animator と同じ規則） ----

        private static CloneContext NewContext() => new CloneContext(GenericPlatformAnimatorBindings.Instance);

        private static VirtualLayer StateLayer(CloneContext context, string name, params bool[] writeDefaults) {
            var layer = VirtualLayer.Create(context, name);
            var sm = VirtualStateMachine.Create(context, name);
            layer.StateMachine = sm;
            for (var i = 0; i < writeDefaults.Length; i++) {
                var state = sm.AddState("State " + i, VirtualClip.Create("Clip " + i));
                state.WriteDefaultValues = writeDefaults[i];
            }
            return layer;
        }

        private static VirtualLayer DirectTreeLayer(CloneContext context, bool writeDefaults) {
            var layer = VirtualLayer.Create(context, "DBT");
            var sm = VirtualStateMachine.Create(context, "DBT");
            layer.StateMachine = sm;
            var tree = VirtualBlendTree.Create("DBT");
            tree.BlendType = BlendTreeType.Direct;
            var state = sm.AddState("DBT", tree);
            state.WriteDefaultValues = writeDefaults;
            sm.DefaultState = state;
            return layer;
        }

        [Test]
        public void SingleStateDirectTreeLayer_RequiresWriteDefaults() {
            var context = NewContext();
            Assert.IsTrue(SpsOutput.IsWriteDefaultsRequired(DirectTreeLayer(context, false)));
            Assert.IsFalse(SpsOutput.IsWriteDefaultsRequired(StateLayer(context, "Toggle", false, false)));
        }

        [Test]
        public void UniformAvatarWriteDefaults_IsDetected() {
            var context = NewContext();
            var controller = VirtualAnimatorController.Create(context, "FX");
            controller.AddLayer(LayerPriority.Default, StateLayer(context, "A", false, false));
            controller.AddLayer(LayerPriority.Default, StateLayer(context, "B", false));
            // ダイレクトブレンドツリーのレイヤーは WD ON でも判定に含めない
            controller.AddLayer(LayerPriority.Default, DirectTreeLayer(context, true));
            Assert.AreEqual(false, SpsOutput.GetUniformWriteDefaults(controller));
        }

        [Test]
        public void MixedAvatarWriteDefaults_IsLeftAlone() {
            var context = NewContext();
            var controller = VirtualAnimatorController.Create(context, "FX");
            controller.AddLayer(LayerPriority.Default, StateLayer(context, "A", false));
            controller.AddLayer(LayerPriority.Default, StateLayer(context, "B", true));
            Assert.IsNull(SpsOutput.GetUniformWriteDefaults(controller));
        }
    }
}
