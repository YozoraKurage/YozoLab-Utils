using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace YozoLab.ChainHandles
{
    /// <summary>
    /// シーンビューのツールバーに置く、Chain Handles のギズモ表示トグル。
    /// 保存してある鎖のハンドルをまとめて出したり消したりする（View Gizmos のようなもの）。
    ///
    /// オーバーレイなので、シーンビュー右上の「︙」メニュー（Overlays）から
    /// 出し入れと置き場所の変更ができる。既定では表示する。
    /// </summary>
    [Overlay(typeof(SceneView), OverlayId, "Chain Handles", true)]
    internal sealed class ChainHandlesOverlay : ToolbarOverlay
    {
        private const string OverlayId = "yozolab-chain-handles";

        private ChainHandlesOverlay() : base(GizmoToggle.Id, OpenButton.Id)
        {
        }

        [EditorToolbarElement(Id, typeof(SceneView))]
        private sealed class GizmoToggle : EditorToolbarToggle
        {
            public const string Id = "YozoLab/ChainHandles/Gizmos";

            public GizmoToggle()
            {
                // 組み込みアイコンは名前がバージョンで変わり、無いと警告が出続けるので文字だけにする。
                text = "Chain";
                tooltip = "Chain Handles のギズモを表示する";

                SetValueWithoutNotify(ChainHandlesScene.Enabled);
                this.RegisterValueChangedCallback(evt => ChainHandlesScene.SetEnabled(evt.newValue));

                RegisterCallback<AttachToPanelEvent>(_ => ChainStore.Changed += Sync);
                RegisterCallback<DetachFromPanelEvent>(_ => ChainStore.Changed -= Sync);
            }

            private void Sync() => SetValueWithoutNotify(ChainHandlesScene.Enabled);
        }

        [EditorToolbarElement(Id, typeof(SceneView))]
        private sealed class OpenButton : EditorToolbarButton
        {
            public const string Id = "YozoLab/ChainHandles/Open";

            public OpenButton()
            {
                text = "一覧";
                tooltip = "鎖の一覧を開く";
                clicked += () => EditorWindow.GetWindow<ChainHandlesWindow>("Chain Handles");
            }
        }
    }
}
