using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace YozoLab.Tests
{
    /// <summary>
    /// Particle Tools が寄りかかっている Unity 内部 API が、まだそこに在るかを見張るテスト。
    ///
    /// Particle Scrubber は時刻指定・再生・再シミュレートを UnityEditor 内部の
    /// ParticleSystemEditorUtils へ委ねている。これは標準の Particle Effect パネルと
    /// 同じ道筋で、サブエミッタが正しく出る唯一の経路でもある。掴めなくなると
    /// ParticleSystem.Simulate による代替へ静かに落ちるので、「壊れた」ではなく
    /// 「黙って見え方が変わる」形で失敗する。それを検知するのがこのテストの役目。
    ///
    /// 意図的に本体（net.yozolab.particletools.Editor）を参照していない。ここで見張るのは
    /// Unity 側の API 表面だけでよく、参照すると Harmony の有無でテストの可否が変わる。
    /// </summary>
    public class ParticleToolsInternalApiTests
    {
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static Assembly EditorAssembly => typeof(Editor).Assembly;

        private static Type GetEditorType(string fullName)
        {
            Type type = EditorAssembly.GetType(fullName);
            Assert.That(type, Is.Not.Null, $"{fullName} が見つからない（Unity のバージョン差異）");
            return type;
        }

        private static PropertyInfo GetStaticProperty(Type type, string name, string legacyName, Type expected)
        {
            PropertyInfo property = type.GetProperty(name, Static) ?? type.GetProperty(legacyName, Static);
            Assert.That(property, Is.Not.Null, $"{type.Name}.{name}（旧 {legacyName}）が無い");
            Assert.That(property.PropertyType, Is.EqualTo(expected), $"{name} の型が {expected.Name} ではない");
            Assert.That(property.GetGetMethod(true), Is.Not.Null, $"{name} の getter が無い");
            Assert.That(property.GetSetMethod(true), Is.Not.Null, $"{name} の setter が無い");
            return property;
        }

        [Test]
        public void ParticleSystemEditorUtils_ExposesPlaybackState()
        {
            Type utils = GetEditorType("UnityEditor.ParticleSystemEditorUtils");

            // 時刻を書いて積み直す、という一連の操作に要るもの。
            GetStaticProperty(utils, "playbackTime", "editorPlaybackTime", typeof(float));
            GetStaticProperty(utils, "playbackIsPlaying", "editorIsPlaying", typeof(bool));
            GetStaticProperty(utils, "playbackIsPaused", "editorIsPaused", typeof(bool));

            // これが落ちていると停止扱いになり、再シミュレートの要求が捨てられる。
            GetStaticProperty(utils, "playbackIsScrubbing", "editorIsScrubbing", typeof(bool));

            GetStaticProperty(utils, "simulationSpeed", "editorSimulationSpeed", typeof(float));
        }

        [Test]
        public void ParticleSystemEditorUtils_ExposesResimulationToggle()
        {
            Type utils = GetEditorType("UnityEditor.ParticleSystemEditorUtils");

            // 標準パネルの Resimulate。掴んでいる間これを切ることで、Inspector を
            // ドラッグしている間の「GUI イベントごとの全ステップ積み直し」を止めている。
            GetStaticProperty(utils, "resimulation", "editorResimulation", typeof(bool));
        }

        [Test]
        public void ParticleSystemEditorUtils_ExposesPreviewOptions()
        {
            Type utils = GetEditorType("UnityEditor.ParticleSystemEditorUtils");

            GetStaticProperty(utils, "previewLayers", "editorPreviewLayers", typeof(uint));
            GetStaticProperty(utils, "renderInSceneView", "editorRenderInSceneView", typeof(bool));
        }

        [Test]
        public void ParticleSystemEditorUtils_CanPerformCompleteResimulation()
        {
            Type utils = GetEditorType("UnityEditor.ParticleSystemEditorUtils");

            MethodInfo resimulate = utils.GetMethod(
                "PerformCompleteResimulation", Static, null, Type.EmptyTypes, null);
            Assert.That(resimulate, Is.Not.Null,
                "PerformCompleteResimulation() が無い（サブエミッタを含む積み直しができない）");
        }

        [Test]
        public void ParticleSystemEffectUtils_CanStopEffect()
        {
            Type effectUtils = GetEditorType("UnityEditor.ParticleSystemEffectUtils");

            MethodInfo stop = effectUtils.GetMethod("StopEffect", Static, null, Type.EmptyTypes, null);
            Assert.That(stop, Is.Not.Null, "StopEffect() が無い");
        }

        [Test]
        public void BuiltinParticleOverlay_HasVisibleGetterToSuppress()
        {
            // 標準パネルは ParticleEffectUI の入れ子オーバーレイ。visible が false なら
            // OnGUI ごと呼ばれないので、そこへ postfix を当てて隠している。
            Type overlay = EditorAssembly.GetType("UnityEditor.ParticleEffectUI+SceneViewParticleOverlay");
            Assert.That(overlay, Is.Not.Null,
                "ParticleEffectUI+SceneViewParticleOverlay が見つからない（標準パネルを隠せない）");

            // DeclaredOnly で見つからないなら override が無いということ。基底の
            // Overlay.visible を掴むと全オーバーレイに効いてしまうので、
            // パッチ側もそのときは当てずに諦める（＝標準パネルが隠れなくなる）。
            PropertyInfo visible = overlay.GetProperty(
                "visible", BindingFlags.Instance | BindingFlags.Public
                           | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.That(visible, Is.Not.Null, "visible の override が無い（標準パネルを隠せない）");

            MethodInfo getter = visible.GetGetMethod(true);
            Assert.That(getter, Is.Not.Null, "visible の getter が無い");
        }

        [Test]
        public void SubEmittersModule_CanEnumerateEmittedSystems()
        {
            // 代替経路では「親のサブエミッタとして駆動される側」を除外する必要がある。
            // 除外できないと、受け側へ直接 Simulate が飛んで粒が壊れる。
            var go = new GameObject("ParticleToolsTest", typeof(ParticleSystem));
            try
            {
                ParticleSystem.SubEmittersModule sub = go.GetComponent<ParticleSystem>().subEmitters;
                Assert.That(sub.subEmittersCount, Is.EqualTo(0));

                MethodInfo getSubEmitter = typeof(ParticleSystem.SubEmittersModule)
                    .GetMethod("GetSubEmitterSystem", new[] { typeof(int) });
                Assert.That(getSubEmitter, Is.Not.Null, "GetSubEmitterSystem(int) が無い");
                Assert.That(getSubEmitter.ReturnType, Is.EqualTo(typeof(ParticleSystem)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
