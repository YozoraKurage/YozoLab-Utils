using System;

namespace YozoLab.UtilSettings
{
    /// <summary>
    /// パッケージ間の「有効なら使える機能」の紐付け 1 件分。
    ///
    /// 利用側の asmdef に、提供側パッケージが有効なあいだだけ Define を注入する。
    /// asmdef の defineConstraints では他 asmdef の有無を判定できないので、
    /// この設定機構が versionDefines を書き換えることで肩代わりする。
    /// </summary>
    internal sealed class FeatureLink
    {
        /// <summary>提供側パッケージの Id。</summary>
        public string ProviderId;

        /// <summary>利用側 asmdef に注入するシンボル。</summary>
        public string Define;
    }

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

        /// <summary>このパッケージのコンパイル可否を決めるシンボル。</summary>
        public string Define;

        /// <summary>設定やウィンドウを開くメニュー項目（無ければ null）。</summary>
        public string OpenMenuPath;

        /// <summary>コンパイルされているときだけ触れる、実行時の ON/OFF。</summary>
        public RuntimeToggle[] Toggles = Array.Empty<RuntimeToggle>();

        /// <summary>このパッケージが利用する、他パッケージ提供の機能。</summary>
        public FeatureLink[] Consumes = Array.Empty<FeatureLink>();
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
                Define = "YOZOLAB_ENABLE_ANIMTOOLS",
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
                Define = "YOZOLAB_ENABLE_VRCGIZMOACCELERATOR",
                OpenMenuPath = "YozoLab/VRC Gizmo Accelerator",
            },
            new UtilPackage
            {
                Id = "pbradiusgizmo",
                DisplayName = "PhysBone Radius Gizmo",
                Description = "PhysBone の Collision Radius をシーン上のハンドルで変える。",
                AsmdefGuid = "98df72da806a4338800e6d264b24df60",
                Define = "YOZOLAB_ENABLE_PBRADIUSGIZMO",
                Consumes = new[]
                {
                    // Accelerator が有効なら、その代替ギズモパスと連携する
                    new FeatureLink
                    {
                        ProviderId = "vrcgizmoaccelerator",
                        Define = "YOZOLAB_HAS_VRCGIZMOACC",
                    },
                },
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
                Define = "YOZOLAB_ENABLE_OPERATIONLOGGER",
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
                Define = "YOZOLAB_ENABLE_FBXANIMATIONBAKER",
                OpenMenuPath = "YozoLab/FBX Animation Baker",
            },
            new UtilPackage
            {
                Id = "fbxanimationextractor",
                DisplayName = "FBX Animation Extractor",
                Description = "FBX からアニメーションを取り出す。",
                AsmdefGuid = "48aa154317f40c64e9da67181b8b3731",
                Define = "YOZOLAB_ENABLE_FBXANIMATIONEXTRACTOR",
                OpenMenuPath = "YozoLab/FBX Animation Extractor",
            },
            new UtilPackage
            {
                Id = "meshbaker",
                DisplayName = "Mesh Baker",
                Description = "メッシュとマテリアルの統合。Editor 側のみ切り替える。",
                AsmdefGuid = "ebd384c51da070704a56b20b6fee94bb",
                Define = "YOZOLAB_ENABLE_MESHBAKER",
                OpenMenuPath = "YozoLab/Frozen Avatar Baker",
            },
            new UtilPackage
            {
                Id = "posebaker",
                DisplayName = "PlayMode Pose Baker",
                Description = "再生中のポーズを焼き込む。NDMF があれば連携する。",
                AsmdefGuid = "655ab3eaed5f9e23cf9fa3fc8264b52d",
                Define = "YOZOLAB_ENABLE_POSEBAKER",
                OpenMenuPath = "YozoLab/PlayMode Pose Baker",
            },
            new UtilPackage
            {
                Id = "casctools",
                DisplayName = "Qrigcasc Generator",
                Description = "Humanoid 向けの qrigcasc 生成。Editor 側のみ切り替える。",
                AsmdefGuid = "8c8503c02fe24d2fb1ec505103eb4f57",
                Define = "YOZOLAB_ENABLE_CASCTOOLS",
                OpenMenuPath = "YozoLab/Qrigcasc Generator",
            },
            new UtilPackage
            {
                Id = "sceneutils",
                DisplayName = "Scene Utils",
                Description = "Window Switcher など、シーン作業まわりの小物。Editor 側のみ切り替える。",
                AsmdefGuid = "6892911967af0bf4baa6600634fbae31",
                Define = "YOZOLAB_ENABLE_SCENEUTILS",
                OpenMenuPath = "Window/Window Switcher",
            },
        };
    }
}
