using System.Linq;
using nadena.dev.ndmf;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using YozoLab.SPS.Component;
using Object = UnityEngine.Object;

namespace YozoLab.SPS.Tests {
    /**
     * NDMF のビルドを実際に通して、SPS がアバターに何をするかを確かめる。
     *
     * 約束は「SPS の分を足すだけで、アバターの既存のものは壊さない」。
     * - 既存の FX のレイヤーと遷移はそのまま残る（VRCFury は読み込み時に遷移などを消していた）
     * - 元のコントローラーのアセットは書き換わらない
     * - SPS のパラメータとメニューは Modular Avatar 経由で入る
     */
    [Category("YozoLab SPS")]
    public class SpsBuildTests {
        private const string TempDir = "Assets/SpsBuildTestsTemp";

        private GameObject avatar;
        private AnimatorController originalFx;
        private AnimationClip socketToggleClip;

        [SetUp]
        public void SetUp() {
            if (!AssetDatabase.IsValidFolder(TempDir)) AssetDatabase.CreateFolder("Assets", "SpsBuildTestsTemp");
        }

        [TearDown]
        public void TearDown() {
            if (avatar != null) Object.DestroyImmediate(avatar);
            AssetDatabase.DeleteAsset(TempDir);
        }

        /**
         * ユーザーのトグル（Socket を出し入れする）を持った FX を用意する。
         * 遷移は条件付きのものと、条件の無い exit time 無しのもの（VRCFury なら消していた壊れた遷移）。
         */
        private void MakeAvatar(bool writeDefaults) {
            avatar = new GameObject("Avatar");
            avatar.AddComponent<Animator>();
            var descriptor = avatar.AddComponent<VRCAvatarDescriptor>();

            var socketObj = new GameObject("Socket");
            socketObj.transform.SetParent(avatar.transform, false);
            socketObj.transform.localPosition = new Vector3(0, 1, 0);

            socketToggleClip = new AnimationClip { name = "Socket On" };
            AnimationUtility.SetEditorCurve(socketToggleClip,
                EditorCurveBinding.FloatCurve("Socket", typeof(GameObject), "m_IsActive"),
                AnimationCurve.Constant(0, 0, 1));
            var offClip = new AnimationClip { name = "Socket Off" };
            AnimationUtility.SetEditorCurve(offClip,
                EditorCurveBinding.FloatCurve("Socket", typeof(GameObject), "m_IsActive"),
                AnimationCurve.Constant(0, 0, 0));

            originalFx = AnimatorController.CreateAnimatorControllerAtPath(TempDir + "/FX.controller");
            AssetDatabase.AddObjectToAsset(socketToggleClip, originalFx);
            AssetDatabase.AddObjectToAsset(offClip, originalFx);
            originalFx.AddParameter("UserToggle", AnimatorControllerParameterType.Bool);
            originalFx.AddLayer("User Toggle");
            var sm = originalFx.layers[1].stateMachine;
            var off = sm.AddState("Off");
            off.motion = offClip;
            off.writeDefaultValues = writeDefaults;
            var on = sm.AddState("On");
            on.motion = socketToggleClip;
            on.writeDefaultValues = writeDefaults;
            var toOn = off.AddTransition(on);
            toOn.AddCondition(AnimatorConditionMode.If, 0, "UserToggle");
            toOn.hasExitTime = false;
            var broken = on.AddTransition(off);
            broken.hasExitTime = false; // 条件も exit time も無い遷移（VRCFury は読み込み時に消していた）
            foreach (var state in originalFx.layers[0].stateMachine.states) state.state.writeDefaultValues = writeDefaults;
            AssetDatabase.SaveAssets();

            descriptor.customizeAnimationLayers = true;
            descriptor.baseAnimationLayers = new[] {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Base, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Additive, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Gesture, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Action, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer {
                    type = VRCAvatarDescriptor.AnimLayerType.FX, isDefault = false, animatorController = originalFx
                },
            };

            descriptor.specialAnimationLayers = new[] {
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.Sitting, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.TPose, isDefault = true },
                new VRCAvatarDescriptor.CustomAnimLayer { type = VRCAvatarDescriptor.AnimLayerType.IKPose, isDefault = true },
            };

            var socket = socketObj.AddComponent<SpsSocket>();
            socket.addLight = SpsSocket.AddLight.Hole;
            socket.addMenuItem = true;
            socket.name = "Test Socket";
        }

        private AnimatorController BuiltFx() {
            return avatar.GetComponent<VRCAvatarDescriptor>().baseAnimationLayers
                .First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX)
                .animatorController as AnimatorController;
        }

        [Test]
        public void Socket_AddsLayersWithoutTouchingExistingOnes() {
            MakeAvatar(writeDefaults: true);
            AvatarProcessor.ProcessAvatar(avatar);

            var built = BuiltFx();
            Assert.NotNull(built);
            Assert.AreNotSame(originalFx, built, "the original controller asset must not be edited in place");

            // 元のアセットはそのまま
            Assert.AreEqual(2, originalFx.layers.Length);
            Assert.AreEqual(2, originalFx.layers[1].stateMachine.states.Length);

            // 既存のレイヤーは先頭のまま、遷移（壊れたものも含めて）も残る
            Assert.AreEqual("Base Layer", built.layers[0].name);
            Assert.AreEqual("User Toggle", built.layers[1].name);
            var builtStates = built.layers[1].stateMachine.states.Select(s => s.state).ToArray();
            Assert.AreEqual(2, builtStates.Length);
            Assert.AreEqual(1, builtStates.First(s => s.name == "Off").transitions.Length);
            Assert.AreEqual(1, builtStates.First(s => s.name == "On").transitions.Length,
                "a transition without conditions must not be removed");

            // SPS のレイヤーが後ろに足される
            Assert.Greater(built.layers.Length, 2);

            // メニューとパラメータは MA 経由で入る
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            Assert.NotNull(descriptor.expressionsMenu);
            Assert.IsTrue(descriptor.expressionsMenu.controls.Any(c => c.name == "SPS"), "SPS menu is installed");
            Assert.NotNull(descriptor.expressionParameters);
            Assert.Greater(descriptor.expressionParameters.parameters.Length, 0);

            // Socket のライトが作られる
            Assert.Greater(avatar.GetComponentsInChildren<Light>(true).Length, 0);
        }

        [Test]
        public void Socket_KeepsUserToggleClipsIntact() {
            MakeAvatar(writeDefaults: true);
            AvatarProcessor.ProcessAvatar(avatar);

            // ユーザーのトグルのクリップ（ビルド後の複製）は、元のカーブを保ったまま
            var built = BuiltFx();
            var onClip = built.layers[1].stateMachine.states.Select(s => s.state).First(s => s.name == "On").motion as AnimationClip;
            Assert.NotNull(onClip);
            Assert.IsTrue(AnimationUtility.GetCurveBindings(onClip)
                .Any(b => b.path == "Socket" && b.type == typeof(GameObject) && b.propertyName == "m_IsActive"));

            // 元のクリップのアセットはそのまま
            Assert.AreEqual(1, AnimationUtility.GetCurveBindings(socketToggleClip).Length);
        }

        [Test]
        public void GeneratedAssets_SurviveAfterTheBuild() {
            // VRCFury は保存していない生成物を次のエディタ更新で破棄していた。プレイモードでは
            // メモリ上のまま使われるので、破棄されないことを確かめる。
            MakeAvatar(writeDefaults: true);
            AvatarProcessor.ProcessAvatar(avatar);
            var built = BuiltFx();
            var spsLayer = built.layers.Skip(2).FirstOrDefault(l => l.stateMachine.states.Length > 0);
            Assert.NotNull(spsLayer);
            Assert.IsTrue(spsLayer.stateMachine != null);
            foreach (var light in avatar.GetComponentsInChildren<Light>(true)) Assert.IsTrue(light != null);
        }

        [Test]
        public void WriteDefaultsOffAvatar_MatchesAndPlacesDefaultsUnderBase() {
            MakeAvatar(writeDefaults: false);
            AvatarProcessor.ProcessAvatar(avatar);

            var built = BuiltFx();
            var defaultsIndex = built.layers.ToList().FindIndex(l => l.name == "SPS Defaults");
            if (defaultsIndex >= 0) {
                Assert.AreEqual(1, defaultsIndex, "SPS Defaults goes right under the base layer");
            }

            // SPS の状態マシンのレイヤーは WD OFF、ダイレクトブレンドツリー 1 つのレイヤーは WD ON
            foreach (var layer in built.layers.Skip(2)) {
                var states = layer.stateMachine.states.Select(s => s.state).ToArray();
                if (states.Length == 1 && states[0].motion is BlendTree tree && tree.blendType == BlendTreeType.Direct
                    && layer.stateMachine.anyStateTransitions.Length == 0 && states[0].transitions.Length == 0) {
                    Assert.IsTrue(states[0].writeDefaultValues, $"{layer.name} (direct blend tree) must be WD on");
                } else if (layer.blendingMode != AnimatorLayerBlendingMode.Additive) {
                    foreach (var s in states) Assert.IsFalse(s.writeDefaultValues, $"{layer.name}/{s.name} should match the avatar (WD off)");
                }
            }
        }

        [Test]
        public void AvatarWithoutSps_IsLeftAlone() {
            MakeAvatar(writeDefaults: true);
            Object.DestroyImmediate(avatar.GetComponentInChildren<SpsSocket>());
            AvatarProcessor.ProcessAvatar(avatar);

            var built = BuiltFx();
            Assert.AreEqual(2, built.layers.Length);
            Assert.IsNull(avatar.transform.Find("YozoLab SPS"));
        }

        [Test]
        public void Plug_PatchesItsShaderForSps() {
            MakeAvatar(writeDefaults: true);
            var plugObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plugObj.name = "Plug";
            Object.DestroyImmediate(plugObj.GetComponent<Collider>());
            plugObj.transform.SetParent(avatar.transform, false);
            plugObj.transform.localPosition = new Vector3(0, 1, 0.3f);
            plugObj.transform.localRotation = Quaternion.Euler(90, 0, 0);
            plugObj.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f); // SPS は拡大率が XYZ で揃っている必要がある
            var material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, TempDir + "/Plug.mat");
            plugObj.GetComponent<MeshRenderer>().sharedMaterial = material;
            plugObj.AddComponent<SpsPlug>();

            // グラフィックの無い環境（-nographics）では、SPS の色の読み取り（GPU からの ReadPixels）が
            // エラーログを出す。SPS の処理自体は続くので、ここでは許容する。
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
            AvatarProcessor.ProcessAvatar(avatar);

            var renderer = avatar.transform.Find("Plug").GetComponentInChildren<Renderer>(true);
            Assert.NotNull(renderer);
            var shader = renderer.sharedMaterials[0].shader;
            StringAssert.StartsWith("Hidden/YozoLabSPSPatched/", shader.name);
            // 元のマテリアルはそのまま
            Assert.AreEqual("Standard", material.shader.name);
        }
    }
}
