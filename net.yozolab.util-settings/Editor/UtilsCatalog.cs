using System;

namespace YozoLab.UtilSettings
{
    /// <summary>
    /// 切り替えの対象になるパッケージ 1 件分の定義。
    /// </summary>
    internal sealed class UtilPackage
    {
        /// <summary>設定ファイルに書き出す識別子。表示名を変えてもこれは変えないこと。</summary>
        public string Id;

        public string DisplayName;
        public string Description;

        /// <summary>
        /// 対象 asmdef の GUID。パスではなく GUID で持つのは、パッケージの置き場所が
        /// 導入方法（VPM / UPM / ローカル）で変わるため。
        /// </summary>
        public string AsmdefGuid;

        /// <summary>
        /// このパッケージのテストアセンブリの asmdef GUID（無ければ null）。
        ///
        /// テスト側は対象アセンブリを参照するので、対象が落ちているのに自分だけ
        /// コンパイルされると参照が解決できず CS0234 で落ちる。かといって
        /// defineConstraints に <see cref="Define"/> を書くだけでは駄目で、
        /// versionDefines はアセンブリごとの設定なので、対象 asmdef で立てた
        /// シンボルはテスト側からは見えない。制約が永久に満たされず、対象を
        /// 有効にしてもテストが走らなくなる。
        ///
        /// そこで対象 asmdef と同じ内容をテスト asmdef にも書き込み、
        /// 有効・無効を揃えて動かす。
        /// </summary>
        public string TestsAsmdefGuid;

        /// <summary>
        /// このパッケージを「コンパイルしない」ことを表すシンボル。
        ///
        /// 対象 asmdef の defineConstraints は否定形（<c>!Define</c>）で書いてある。
        /// つまり誰も立てなければコンパイルされる。この設定機構はシンボルを
        /// 立てて「切る」ためだけに使い、有効化とは asmdef を出荷時の姿へ戻すこと。
        ///
        /// この向きにしてあるのは、各アドオンが util-settings 抜きでも単体で
        /// 成立している必要があるため。有効化が必要な形（!無しで ENABLE を要求）
        /// だと、この設定機構が入っていないプロジェクトでは 1 つもコンパイル
        /// されず、アドオンだけを取り出して使えない。
        /// </summary>
        public string Define;


        /// <summary>設定やウィンドウを開くメニュー項目（無ければ null）。</summary>
        public string OpenMenuPath;

        /// <summary>コンパイルされているときだけ触れる、実行時の ON/OFF。</summary>
        public RuntimeToggle[] Toggles = Array.Empty<RuntimeToggle>();
    }

    /// <summary>
    /// 実行時トグル 1 件分。<c>public static bool Enabled</c> と
    /// <c>static void SetEnabled(bool)</c> を持つ型を、リフレクションで叩く。
    ///
    /// 型を直接参照しないのは、対象アセンブリがコンパイルされていない状態が
    /// 正常系だから。参照してしまうとこのウィンドウ自身が巻き添えで壊れる。
    /// </summary>
    internal sealed class RuntimeToggle
    {
        public string Label;
        public string TypeName;
        public string Tooltip;
    }

    /// <summary>
    /// このリポジトリが提供するパッケージの一覧。
    ///
    /// Editor アセンブリだけを対象にしている。Runtime 側（meshbaker・sceneutils・
    /// casctools）を落とすとシーンやプレハブが参照している MonoBehaviour の型が
    /// 消え、既存の資産が壊れる。エディタ拡張としての動作は Editor 側を止めれば
    /// 全て止まるので、あえて触らない。
    /// </summary>
    internal static class UtilsCatalog
    {
        public static readonly UtilPackage[] Packages =
        {
            new UtilPackage
            {
                Id = "animtools",
                DisplayName = "Animation Tools",
                Description = "Animation ウィンドウまわりの拡張。Harmony を使う。",
                AsmdefGuid = "6b8d0f08c02241c78f4bd111ed92fc21",
                TestsAsmdefGuid = "31d72fe163d8e99bbac8794dd6dd532c",
                Define = "YOZOLAB_DISABLE_ANIMTOOLS",
                Toggles = new[]
                {
                    new RuntimeToggle
                    {
                        Label = "前フレーム自動キー (レコード時)",
                        TypeName = "YozoLab.AnimTools.HoldPreviousKeyRecorder",
                        Tooltip = "レコード時、打ったキーの1フレーム前に直前の値を自動で打ち込む。",
                    },
                    new RuntimeToggle
                    {
                        Label = "Humanoid キーを隠す (擬似レイヤー)",
                        TypeName = "YozoLab.AnimTools.HumanoidLayerFilter",
                        Tooltip = "Humanoid のキーを Animation ウィンドウから隠し、レコード中の書き込みも禁止する。",
                    },
                    new RuntimeToggle
                    {
                        Label = "ブレンドシェイプ名を短く表示",
                        TypeName = "YozoLab.AnimTools.BlendShapeRowName",
                        Tooltip = "行頭の「Skinned Mesh Renderer.Blend Shape.」を省く。",
                    },
                },
            },
            new UtilPackage
            {
                Id = "vrcgizmoaccelerator",
                DisplayName = "VRC Gizmo Accelerator",
                Description = "PhysBone ギズモを独自の一括描画パスに置き換えて軽くする。Harmony を使う。",
                AsmdefGuid = "c54f7afe8bca44b5bf2680c2058b79b5",
                TestsAsmdefGuid = "12a0b490157839ef79a1addefcf8f301",
                Define = "YOZOLAB_DISABLE_VRCGIZMOACCELERATOR",
                OpenMenuPath = "YozoLab/VRC Gizmo Accelerator",
            },
            new UtilPackage
            {
                Id = "pbradiusgizmo",
                DisplayName = "PhysBone Radius Gizmo",
                Description = "PhysBone の Collision Radius をシーン上のハンドルで変える。",
                AsmdefGuid = "98df72da806a4338800e6d264b24df60",
                TestsAsmdefGuid = "d58808ea513ef82e991b03714d812d34",
                Define = "YOZOLAB_DISABLE_PBRADIUSGIZMO",
                Toggles = new[]
                {
                    new RuntimeToggle
                    {
                        Label = "Collision Radius をシーンで操作",
                        TypeName = "YozoLab.PBRadiusGizmo.PhysBoneRadiusGizmo",
                        Tooltip = "PhysBone を選ぶと Radius のハンドルが出る。",
                    },
                },
            },
            new UtilPackage
            {
                Id = "operationlogger",
                DisplayName = "Operation Logger",
                Description = "エディタ操作の記録。Harmony を使う。",
                AsmdefGuid = "086be9f94816f2341a7304f955722ab7",
                Define = "YOZOLAB_DISABLE_OPERATIONLOGGER",
                Toggles = new[]
                {
                    new RuntimeToggle
                    {
                        Label = "記録する",
                        TypeName = "YozoLab.OperationLogger.OpLogger",
                        Tooltip = "エディタ操作の記録を開始/停止する。",
                    },
                },
            },
            new UtilPackage
            {
                Id = "fbxanimationbaker",
                DisplayName = "FBX Animation Baker",
                Description = "アニメーションを焼き込んだ FBX を書き出す。",
                AsmdefGuid = "cfe0f2ee4c194abbaee7f96b07cbcefa",
                TestsAsmdefGuid = "2dd2f79c2d81fc7a2932776149e3913d",
                Define = "YOZOLAB_DISABLE_FBXANIMATIONBAKER",
                OpenMenuPath = "YozoLab/FBX Animation Baker",
            },
            new UtilPackage
            {
                Id = "fbxanimationextractor",
                DisplayName = "FBX Animation Extractor",
                Description = "FBX からアニメーションを取り出す。",
                AsmdefGuid = "48aa154317f40c64e9da67181b8b3731",
                Define = "YOZOLAB_DISABLE_FBXANIMATIONEXTRACTOR",
                OpenMenuPath = "YozoLab/FBX Animation Extractor",
            },
            new UtilPackage
            {
                Id = "meshbaker",
                DisplayName = "Mesh Baker",
                Description = "メッシュとマテリアルの統合。Editor 側のみ切り替える。",
                AsmdefGuid = "ebd384c51da070704a56b20b6fee94bb",
                Define = "YOZOLAB_DISABLE_MESHBAKER",
                OpenMenuPath = "YozoLab/Frozen Avatar Baker",
            },
            new UtilPackage
            {
                Id = "posebaker",
                DisplayName = "PlayMode Pose Baker",
                Description = "再生中のポーズを焼き込む。NDMF があれば連携する。",
                AsmdefGuid = "655ab3eaed5f9e23cf9fa3fc8264b52d",
                Define = "YOZOLAB_DISABLE_POSEBAKER",
                OpenMenuPath = "YozoLab/PlayMode Pose Baker",
            },
            new UtilPackage
            {
                Id = "casctools",
                DisplayName = "Qrigcasc Generator",
                Description = "Humanoid 向けの qrigcasc 生成。Editor 側のみ切り替える。",
                AsmdefGuid = "8c8503c02fe24d2fb1ec505103eb4f57",
                Define = "YOZOLAB_DISABLE_CASCTOOLS",
                OpenMenuPath = "YozoLab/Qrigcasc Generator",
            },
            new UtilPackage
            {
                Id = "sceneutils",
                DisplayName = "Scene Utils",
                Description = "Window Switcher など、シーン作業まわりの小物。Editor 側のみ切り替える。",
                AsmdefGuid = "6892911967af0bf4baa6600634fbae31",
                Define = "YOZOLAB_DISABLE_SCENEUTILS",
                OpenMenuPath = "Window/Window Switcher",
            },
            new UtilPackage
            {
                Id = "particletools",
                DisplayName = "Particle Tools",
                Description = "パーティクル制作支援。時間バーでのスクラブ(標準 Particle Effect パネル置き換え)と色の一括編集。",
                AsmdefGuid = "998afc256cc14f59adb8fb9bd4245156",
                TestsAsmdefGuid = "fdb1e1857182a0d938a4c01c93383726",
                Define = "YOZOLAB_DISABLE_PARTICLETOOLS",
                OpenMenuPath = "YozoLab/Particle Color Editor",
            },
        };
    }
}
