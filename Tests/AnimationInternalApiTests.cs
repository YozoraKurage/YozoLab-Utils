using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

namespace YozoLab.Tests
{
    /// <summary>
    /// Animation ウィンドウ拡張が寄りかかっている Unity 内部 API が、まだそこに在るかを見張るテスト。
    ///
    /// HumanoidLayerFilter・HoldPreviousKeyRecorder・AnimationWindowClipPing は
    /// UnityEditor の internal な型へリフレクションと Harmony で手を入れている。これらは
    /// 実行時に静かに無効化される作りなので、Unity を上げた際「壊れた」ではなく「黙って
    /// 効かなくなる」形で失敗する。それを検知するのがこのテストの役目。
    ///
    /// 意図的に本体（net.yozolab.animtools.Editor）を参照していない。Harmony が無い環境では
    /// 本体が丸ごとコンパイルされないため、参照すると VRCSDK の有無でテストの可否が変わって
    /// しまう。ここで見張るのは Unity 側の API 表面だけでよい。
    /// </summary>
    public class AnimationInternalApiTests
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        private static Assembly EditorAssembly => typeof(Editor).Assembly;

        private static Type GetEditorType(string fullName)
        {
            Type type = EditorAssembly.GetType(fullName);
            Assert.That(type, Is.Not.Null, $"{fullName} が見つからない（Unity のバージョン差異）");
            return type;
        }

        [Test]
        public void AnimationWindowState_allCurves_とキャッシュ場所が存在する()
        {
            Type state = GetEditorType("UnityEditorInternal.AnimationWindowState");

            PropertyInfo allCurves = state.GetProperty("allCurves", Instance);
            Assert.That(allCurves, Is.Not.Null, "allCurves プロパティが無い");
            Assert.That(allCurves.GetGetMethod(true), Is.Not.Null, "allCurves の getter が無い");

            // 表示フィルタはこのキャッシュを絞り込み済みリストへ差し替えることで成立している。
            FieldInfo cache = state.GetField("m_AllCurvesCache", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(cache, Is.Not.Null, "m_AllCurvesCache フィールドが無い");
            Assert.That(typeof(IList).IsAssignableFrom(cache.FieldType),
                "m_AllCurvesCache が IList ではない（差し替えリストを組み立てられない）");
            Assert.That(cache.FieldType.GetConstructor(Type.EmptyTypes), Is.Not.Null,
                "m_AllCurvesCache の型に引数なしコンストラクタが無い（同じ型の空リストを作れない）");
        }

        [Test]
        public void AnimationWindowState_refresh_で作り直しを要求できる()
        {
            Type state = GetEditorType("UnityEditorInternal.AnimationWindowState");

            PropertyInfo refresh = state.GetProperty("refresh", Instance);
            Assert.That(refresh, Is.Not.Null, "refresh プロパティが無い");
            Assert.That(refresh.GetSetMethod(true), Is.Not.Null, "refresh の setter が無い");

            Type refreshType = state.GetNestedType("RefreshType", BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(refreshType, Is.Not.Null, "RefreshType が無い");
            Assert.That(Enum.GetNames(refreshType), Contains.Item("Everything"),
                "RefreshType.Everything が無い（トグル後に表示を作り直せない）");
        }

        [Test]
        public void AnimationWindowCurve_から_binding_を読める()
        {
            Type curve = GetEditorType("UnityEditorInternal.AnimationWindowCurve");

            PropertyInfo binding = curve.GetProperty("binding", Instance);
            Assert.That(binding, Is.Not.Null, "binding プロパティが無い");
            Assert.That(binding.PropertyType, Is.EqualTo(typeof(EditorCurveBinding)),
                "binding が EditorCurveBinding ではない（Humanoid 判定ができない）");
        }

        [Test]
        public void AnimationRecording_のキー書き込み経路が二つとも存在する()
        {
            Type recording = GetEditorType("UnityEditorInternal.AnimationRecording");

            // (1) 通常のキー追加。EditorCurveBinding を受け取るものだけが対象。
            MethodInfo[] addKeys = recording.GetMethods(Static)
                .Where(m => m.Name == "AddKey" || m.Name == "AddRotationKey")
                .Where(m => m.GetParameters().Any(p => p.ParameterType == typeof(EditorCurveBinding)))
                .ToArray();
            Assert.That(addKeys, Is.Not.Empty,
                "EditorCurveBinding を引数に取る AddKey / AddRotationKey が無い");
            Assert.That(addKeys.All(m => m.GetParameters().Any(p => p.Name == "binding")),
                "引数名が binding ではない（Harmony が値をインジェクトできない）");

            // (2) ルートモーション。AddKey を経由せず自前で Animator 型バインディングを
            //     組み立てて書き込むため、ここを塞がないと録画ロックに穴が空く。
            MethodInfo rootMotion = recording.GetMethod("ProcessRootMotionModification", Static);
            Assert.That(rootMotion, Is.Not.Null,
                "ProcessRootMotionModification が無い（ルートモーションの書き込みを塞げない）");
        }

        [Test]
        public void AnimationWindow_の描画と状態への入口が存在する()
        {
            Type window = GetEditorType("UnityEditor.AnimationWindow");

            MethodInfo onGui = window.GetMethod("OnGUI", Instance, null, Type.EmptyTypes, null);
            Assert.That(onGui, Is.Not.Null, "引数なしの OnGUI が無い（ツールバーのボタンを描けない）");

            FieldInfo animEditor = window.GetField("m_AnimEditor", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(animEditor, Is.Not.Null, "m_AnimEditor が無い（AnimationWindowState へ辿れない）");

            PropertyInfo state = animEditor.FieldType.GetProperty("state", Instance);
            Assert.That(state, Is.Not.Null, "AnimEditor.state が無い（AnimationWindowState へ辿れない）");
        }
    }
}
